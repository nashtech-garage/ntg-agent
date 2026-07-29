# Cloud VM Setup Guide — Docker TLS + LightRAG (Ubuntu 24.04 LTS)

Applies to: **Azure VM**, **AWS EC2**, **GCP Compute Engine**  
Assumption: the VM is reachable via SSH on **port 22** with a public IP address.

---

## Table of Contents

1. [Install Docker Engine and Docker Compose](#1-install-docker-engine-and-docker-compose)
2. [Server Prep for LightRAG](#2-server-prep-for-lightrag)
3. [Open Inbound Port Range 20000–20999 (LightRAG Agents)](#3-open-inbound-port-range-20000-20999-lightrag-agents)
4. [Generate Self-Signed TLS Certificates](#4-generate-self-signed-tls-certificates)
5. [Configure Docker Daemon with TLS](#5-configure-docker-daemon-with-tls)
6. [Open Inbound Port 2376 (Docker TLS)](#6-open-inbound-port-2376-docker-tls)
7. [Download the Client Certificate](#7-download-the-client-certificate)
8. [Sample C# Code — Connect to Docker via TLS](#8-sample-c-code--connect-to-docker-via-tls)

---

## 1. Install Docker Engine and Docker Compose

SSH into the VM and run the following commands.

```bash
ssh <user>@<PUBLIC_IP>
```

### 1a. Remove any old Docker packages

```bash
for pkg in docker.io docker-doc docker-compose docker-compose-v2 podman-docker containerd runc; do
  sudo apt-get remove -y $pkg 2>/dev/null
done
```

### 1b. Add Docker's official APT repository

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl

sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
     -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update
```

### 1c. Install Docker Engine and Docker Compose plugin

```bash
sudo apt-get install -y \
  docker-ce docker-ce-cli containerd.io \
  docker-buildx-plugin docker-compose-plugin
```

### 1d. Verify the installation

```bash
sudo docker run hello-world
docker compose version
```

### 1e. Allow your SSH user to run Docker without `sudo`

> Replace `ntgagent` with your actual username if different.

```bash
sudo usermod -aG docker $USER
# Log out and log back in for the group change to take effect
exit
ssh <user>@<PUBLIC_IP>
# Confirm - should show docker group
groups
```

---

## 2. Server Prep for LightRAG

```bash
# Clone the repository (the compose build context needs scripts/)
git clone https://github.com/nashtech-garage/ntg-agent.git ntg-agent

# Navigate to the LightRAG Postgres deployment
cd ntg-agent/deploy/lightrag-postgres

# Copy the example env file and set your Postgres password
cp .env.example .env
nano .env    # set POSTGRES_PASSWORD to a strong value

# Start Postgres in the background
docker compose up -d

# Verify: Postgres binds to loopback only (127.0.0.1:5432)
docker compose ps
```

> **Note:** Postgres is intentionally only accessible on `127.0.0.1:5432` — it is **not** exposed publicly.

---

## 3. Open Inbound Port Range 20000–20999 (LightRAG Agents)

LightRAG agent containers are assigned ports in the `20000–20999` range.

### Azure — Network Security Group (NSG)

```
Portal → Virtual Machine → Networking → Network Security Group
→ Inbound security rules → Add

Source:           IP Addresses (enter your client IP, or * for any)
Source port:      *
Destination:      Any
Destination port: 20000-20999
Protocol:         TCP
Action:           Allow
Priority:         310
Name:             Allow-LightRAG-Agents
```

Or via Azure CLI:

```bash
az network nsg rule create \
  --resource-group <RG_NAME> \
  --nsg-name <NSG_NAME> \
  --name Allow-LightRAG-Agents \
  --priority 310 \
  --protocol Tcp \
  --destination-port-ranges 20000-20999 \
  --access Allow \
  --direction Inbound
```

### AWS — Security Group

```
EC2 Console → Security Groups → Select your group → Inbound rules → Edit inbound rules → Add rule

Type:        Custom TCP
Port range:  20000 - 20999
Source:      My IP  (or a specific CIDR, e.g. 203.0.113.0/24)
Description: LightRAG agents
```

Or via AWS CLI:

```bash
aws ec2 authorize-security-group-ingress \
  --group-id <SG_ID> \
  --protocol tcp \
  --port 20000-20999 \
  --cidr <YOUR_IP>/32
```

### GCP — Firewall Rule

```bash
gcloud compute firewall-rules create allow-lightrag-agents \
  --direction=INGRESS \
  --priority=1000 \
  --network=default \
  --action=ALLOW \
  --rules=tcp:20000-20999 \
  --source-ranges=<YOUR_IP>/32 \
  --target-tags=<YOUR_VM_TAG>
```

---

## 4. Generate Self-Signed TLS Certificates

Run all commands **on the VM** inside a dedicated directory.

```bash
mkdir -p ~/docker-certs && cd ~/docker-certs
```

> **Replace `<PUBLIC_IP>` with the actual VM public IP address** (e.g. `4.193.109.6`) in the commands below.

### 4a. Certificate Authority (CA)

```bash
# Generate CA private key (you will be prompted for a passphrase — remember it)
openssl genrsa -aes256 -passout pass:capassword -out ca-key.pem 4096

# Generate CA self-signed certificate (valid 825 days)
openssl req -new -x509 -days 825 \
  -key ca-key.pem -passin pass:capassword \
  -sha256 -out ca.pem \
  -subj "/CN=docker-ca"
```

### 4b. Server Certificate (signed by your CA)

```bash
# Generate server private key
openssl genrsa -out server-key.pem 4096

# Generate server certificate signing request (CSR)
openssl req -subj "/CN=<PUBLIC_IP>" -sha256 -new \
  -key server-key.pem -out server.csr

# Create extension file — include the VM public IP as a Subject Alternative Name
cat > extfile.cnf <<EOF
subjectAltName = IP:<PUBLIC_IP>,IP:127.0.0.1
extendedKeyUsage = serverAuth
EOF

# Sign the server certificate with your CA
openssl x509 -req -days 825 -sha256 \
  -in server.csr \
  -CA ca.pem -CAkey ca-key.pem -passin pass:capassword \
  -CAcreateserial \
  -out server-cert.pem -extfile extfile.cnf
```

### 4c. Client Certificate (signed by your CA)

```bash
# Generate client private key
openssl genrsa -out key.pem 4096

# Generate client CSR
openssl req -subj '/CN=client' -new -key key.pem -out client.csr

# Create extension file for client auth
echo "extendedKeyUsage = clientAuth" > extfile-client.cnf

# Sign the client certificate with your CA
openssl x509 -req -days 825 -sha256 \
  -in client.csr \
  -CA ca.pem -CAkey ca-key.pem -passin pass:capassword \
  -CAcreateserial \
  -out cert.pem -extfile extfile-client.cnf
```

### 4d. Export client cert to PFX (required by C# / .NET)

```bash
openssl pkcs12 -export \
  -inkey key.pem \
  -in cert.pem \
  -out client.pfx \
  -passout pass:YourPfxPassword123
```

> **Remember this PFX password** — you will need it in the C# code (Step 8).

### 4e. Lock down file permissions

```bash
chmod 0400 ca-key.pem server-key.pem key.pem
chmod 0444 ca.pem server-cert.pem cert.pem client.pfx
```

### 4f. Clean up temporary files

```bash
rm -f server.csr client.csr extfile.cnf extfile-client.cnf
```

---

## 5. Configure Docker Daemon with TLS

### 5a. Write `/etc/docker/daemon.json`

```bash
sudo tee /etc/docker/daemon.json > /dev/null <<EOF
{
  "hosts":      ["unix:///var/run/docker.sock", "tcp://0.0.0.0:2376"],
  "tls":        true,
  "tlsverify":  true,
  "tlscacert":  "/home/$USER/docker-certs/ca.pem",
  "tlscert":    "/home/$USER/docker-certs/server-cert.pem",
  "tlskey":     "/home/$USER/docker-certs/server-key.pem"
}
EOF
```

> **Note:** Run `echo $USER` first to confirm the username, then replace `$USER` with the actual value if needed (e.g. `/home/azureuser/docker-certs/...`).

### 5b. Override the systemd unit

The default `docker.service` unit passes `-H fd://` which conflicts with the `hosts` key in `daemon.json`. Override it:

```bash
sudo mkdir -p /etc/systemd/system/docker.service.d

sudo tee /etc/systemd/system/docker.service.d/override.conf > /dev/null <<'EOF'
[Service]
ExecStart=
ExecStart=/usr/bin/dockerd
EOF
```

### 5c. Reload and restart Docker

```bash
sudo systemctl daemon-reload
sudo systemctl restart docker
```

### 5d. Verify Docker is listening on port 2376

```bash
sudo ss -tlnp | grep 2376
# Expected output:
# LISTEN 0  4096  0.0.0.0:2376  0.0.0.0:*  users:(("dockerd",...))
```

---

## 6. Open Inbound Port 2376 (Docker TLS)

> **Important:** Restrict the source to **your specific IP address only**. Exposing Docker's API publicly to `0.0.0.0/0` gives full control of the host to anyone.

### Azure — NSG

```
Portal → Virtual Machine → Networking → Network Security Group
→ Inbound security rules → Add

Source:           IP Addresses (enter your client IP)
Source port:      *
Destination port: 2376
Protocol:         TCP
Action:           Allow
Priority:         320
Name:             Allow-Docker-TLS
```

Or via Azure CLI:

```bash
az network nsg rule create \
  --resource-group <RG_NAME> \
  --nsg-name <NSG_NAME> \
  --name Allow-Docker-TLS \
  --priority 320 \
  --protocol Tcp \
  --destination-port-ranges 2376 \
  --source-address-prefixes <YOUR_IP>/32 \
  --access Allow \
  --direction Inbound
```

### AWS — Security Group

```bash
aws ec2 authorize-security-group-ingress \
  --group-id <SG_ID> \
  --protocol tcp \
  --port 2376 \
  --cidr <YOUR_IP>/32
```

### GCP — Firewall Rule

```bash
gcloud compute firewall-rules create allow-docker-tls \
  --direction=INGRESS \
  --priority=1000 \
  --network=default \
  --action=ALLOW \
  --rules=tcp:2376 \
  --source-ranges=<YOUR_IP>/32 \
  --target-tags=<YOUR_VM_TAG>
```

---

## 7. Download the Client Certificate

Copy `client.pfx` from the VM to your local machine.

### From Windows (PowerShell)

```powershell
# Create the certs folder in your project
New-Item -ItemType Directory -Force -Path C:\Projects\ntg-agent\certs

# SCP the file from the VM
scp <user>@<PUBLIC_IP>:~/docker-certs/client.pfx C:\Projects\ntg-agent\certs\client.pfx
```

### From macOS / Linux

```bash
scp <user>@<PUBLIC_IP>:~/docker-certs/client.pfx ./certs/client.pfx
```

### Using an SSH key (if password auth is disabled)

```powershell
scp -i C:\path\to\your\key.pem <user>@<PUBLIC_IP>:~/docker-certs/client.pfx .\certs\client.pfx
```

---

## 8. Sample C# Code — Connect to Docker via TLS

### 8a. NuGet packages required

```xml
<PackageReference Include="Docker.DotNet"      Version="3.125.15" />
<PackageReference Include="Docker.DotNet.X509" Version="3.125.15" />
```

Add them via CLI:

```bash
dotnet add package Docker.DotNet
dotnet add package Docker.DotNet.X509
```

### 8b. Program.cs

```csharp
using System.Security.Cryptography.X509Certificates;
using Docker.DotNet;
using Docker.DotNet.Models;
using Docker.DotNet.X509;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
const string certPath = "certs/client.pfx";
string certPassword   = Environment.GetEnvironmentVariable("DOCKER_CERT_PASS") ?? "YourPfxPassword123";

if (!File.Exists(certPath))
{
    Console.Error.WriteLine($"ERROR: Client certificate not found at '{certPath}'.");
    Console.Error.WriteLine("Run the OpenSSL steps in the guide and copy client.pfx to the certs/ folder.");
    return;
}

// Load the PFX client certificate
var cert        = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
var credentials = new CertificateCredentials(cert);

// Accept the self-signed server certificate (it was signed by your own CA)
credentials.ServerCertificateValidationCallback += (_, _, _, _) => true;

// ---------------------------------------------------------------------------
// Connect to the remote Docker daemon over HTTPS (TLS) on port 2376
// ---------------------------------------------------------------------------
var uri    = new Uri("https://<PUBLIC_IP>:2376");   // replace <PUBLIC_IP>
var client = new DockerClientConfiguration(uri, credentials).CreateClient();

Console.WriteLine($"Connected to Docker daemon at {uri}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// docker images
// ---------------------------------------------------------------------------
Console.WriteLine("=== Docker Images ===");
IList<ImagesListResponse> images = await client.Images.ListImagesAsync(
    new ImagesListParameters { All = false });

if (images.Count == 0)
{
    Console.WriteLine("  (no images)");
}
else
{
    Console.WriteLine($"  {"REPOSITORY",-40} {"TAG",-20} {"IMAGE ID",-15} {"SIZE",10}");
    Console.WriteLine(new string('-', 90));
    foreach (var img in images)
    {
        string repo    = img.RepoTags?.FirstOrDefault()?.Split(':')[0] ?? "<none>";
        string tag     = img.RepoTags?.FirstOrDefault()?.Split(':').ElementAtOrDefault(1) ?? "<none>";
        string imageId = img.ID.Replace("sha256:", "")[..12];
        string size    = FormatBytes(img.Size);
        Console.WriteLine($"  {repo,-40} {tag,-20} {imageId,-15} {size,10}");
    }
}

Console.WriteLine();

// ---------------------------------------------------------------------------
// docker ps  (running containers)
// ---------------------------------------------------------------------------
Console.WriteLine("=== Running Containers (docker ps) ===");
IList<ContainerListResponse> containers = await client.Containers.ListContainersAsync(
    new ContainersListParameters { All = false });   // All = false → only running containers

if (containers.Count == 0)
{
    Console.WriteLine("  (no running containers)");
}
else
{
    Console.WriteLine($"  {"CONTAINER ID",-14} {"IMAGE",-30} {"STATUS",-20} {"NAMES",-25} {"PORTS"}");
    Console.WriteLine(new string('-', 110));
    foreach (var c in containers)
    {
        string id     = c.ID[..12];
        string image  = c.Image.Length > 28 ? c.Image[..28] + ".." : c.Image;
        string names  = string.Join(", ", c.Names.Select(n => n.TrimStart('/')));
        string ports  = string.Join(", ", c.Ports.Select(p =>
            p.PublicPort > 0 ? $"{p.IP}:{p.PublicPort}->{p.PrivatePort}/{p.Type}"
                             : $"{p.PrivatePort}/{p.Type}"));
        Console.WriteLine($"  {id,-14} {image,-30} {c.Status,-20} {names,-25} {ports}");
    }
}

client.Dispose();

// ---------------------------------------------------------------------------
static string FormatBytes(long bytes)
{
    if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
    if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
    if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
    return $"{bytes} B";
}
```

### 8c. Run the app

```powershell
# Set the PFX password as an environment variable (avoid hardcoding it)
$env:DOCKER_CERT_PASS = "YourPfxPassword123"
dotnet run
```

---

## Quick Checklist

| # | Task | Verify |
|---|------|--------|
| 1 | Docker Engine installed | `docker --version` |
| 2 | User added to `docker` group | `groups \| grep docker` |
| 3 | LightRAG Postgres stack running | `docker compose ps` (in `ntg-agent/deploy/lightrag-postgres`) |
| 4 | Ports 20000–20999 open in cloud firewall | `nc -zv <PUBLIC_IP> 20000` |
| 5 | Certificates generated in `~/docker-certs/` | `ls ~/docker-certs/*.pem *.pfx` |
| 6 | Docker daemon listening on TLS | `sudo ss -tlnp \| grep 2376` |
| 7 | Port 2376 open in cloud firewall (your IP only) | `nc -zv <PUBLIC_IP> 2376` from local machine |
| 8 | `client.pfx` copied to local `certs/` folder | `Test-Path certs/client.pfx` (PowerShell) |
| 9 | C# app connects and lists images/containers | `dotnet run` |

---

## Security Reminders

- **Never expose port 2376 to `0.0.0.0/0`** — always restrict to your specific IP. The Docker API gives full root-equivalent access to the host.
- Store `DOCKER_CERT_PASS` in a secrets manager or environment variable, never hardcoded in source code.
- Rotate certificates before they expire (default: 825 days in this guide).
- The `ca-key.pem` and `server-key.pem` files on the VM should remain readable only by `root` (`chmod 0400`).
