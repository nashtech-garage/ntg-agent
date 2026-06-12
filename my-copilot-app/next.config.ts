import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  experimental: {
    // The dev filesystem cache (on by default in Next 16) persists compiled state
    // in .next/dev across restarts. On /mnt/d (NTFS via WSL2) it goes stale and
    // randomly drops routes (e.g. /api/copilotkit/* returning 404 after a restart),
    // so recompute from scratch on every dev start.
    turbopackFileSystemCacheForDev: false,
  },
};

export default nextConfig;
