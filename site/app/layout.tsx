import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono } from "next/font/google";
import { ThemeProvider } from "@/components/theme-provider";
import "./globals.css";

/*
  Edgepad itself sets every piece of text in JetBrains Mono. The site keeps that for
  everything structural — headings, labels, tables, code, navigation — so it reads as
  the same product, and falls back to a neutral sans for running prose only, because
  several thousand words of mono is harder to read than it is characterful.
*/
const mono = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-mono-family",
  display: "swap",
});

const sans = Inter({
  subsets: ["latin"],
  variable: "--font-sans-family",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Edgepad — documentation",
  description:
    "Edgepad turns an Android phone into a trackpad, a media remote and a control panel for a Windows laptop, over a direct Bluetooth link with nothing in between. Full documentation: architecture, wire protocol, install, and the developer guide.",
  applicationName: "Edgepad",
  authors: [{ name: "akshit-bansal11" }],
  openGraph: {
    title: "Edgepad — documentation",
    description:
      "Phone as a control surface for a Windows laptop, over Bluetooth Classic RFCOMM. No account, no Wi-Fi, no server.",
    type: "website",
  },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#ffffff" },
    { media: "(prefers-color-scheme: dark)", color: "#000000" },
  ],
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className={`${mono.variable} ${sans.variable} font-sans`}>
        <ThemeProvider
          attribute="class"
          defaultTheme="system"
          enableSystem
          disableTransitionOnChange
        >
          {children}
        </ThemeProvider>
      </body>
    </html>
  );
}
