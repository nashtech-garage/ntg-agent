import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Required by Aspire's AddNextJsApp publish mode (containerized standalone output).
  output: "standalone",
};

export default nextConfig;
