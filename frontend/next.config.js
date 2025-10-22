/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  output: 'standalone',
  async rewrites() {
    return [
      {
        source: '/api/auth/:path*',
        destination: '/api/proxy/auth/:path*',
      },
    ];
  },
};

module.exports = nextConfig;
