export const metadata = {
  title: 'HANYA.KNOW',
  description: 'HANYA.KNOW application',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
