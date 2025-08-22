import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  /* config options here */
  reactStrictMode: true,
  output: "standalone",
  experimental: {
    externalDir: true,
  },
};

export default nextConfig;
