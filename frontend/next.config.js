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
      {
        source: '/demos/:path*',
        destination: `${process.env.API_BASE_URL || 'http://localhost:5000'}/demos/:path*`,
      },
    ];
  },
};

module.exports = nextConfig;
