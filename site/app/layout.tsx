import type { Metadata, Viewport } from "next";
import { JetBrains_Mono, Lato } from "next/font/google";
import { ThemeProvider } from "@/components/theme-provider";
import { SITE, SOCIAL_IMAGE } from "@/lib/nav";
import "./globals.css";

/*
  Edgepad 2.0 sets every piece of text in Lato, in three weights: 400 for reading, 700
  for labels and buttons, 900 for titles. JetBrains Mono stays for what really is code:
  commands, byte tables, protocol ids.
*/
const sans = Lato({
  subsets: ["latin"],
  weight: ["400", "700", "900"],
  variable: "--font-sans-family",
  display: "swap",
});

const mono = JetBrains_Mono({
  subsets: ["latin"],
  variable: "--font-mono-family",
  display: "swap",
});

export const metadata: Metadata = {
  // Every relative URL in metadata (Open Graph, canonical) resolves against this.
  metadataBase: new URL(SITE),
  // The landing page's title is the default; app/docs/page.tsx overrides it. The
  // canonical link is each page's own, so a future page never inherits "/" by accident.
  title: "Edgepad — your laptop, from across the room",
  description:
    "Edgepad turns an Android phone into a trackpad, a media remote and a control panel for a Windows laptop, over a direct Bluetooth link with nothing in between. No account, no network, no telemetry. MIT.",
  applicationName: "Edgepad",
  authors: [{ name: "akshit-bansal11" }],
  openGraph: {
    title: "Edgepad — your laptop, from across the room",
    description:
      "Phone as a control surface for a Windows laptop, over Bluetooth Classic RFCOMM. No account, no Wi-Fi, no server.",
    type: "website",
    url: "/",
    siteName: "Edgepad",
    images: [SOCIAL_IMAGE],
  },
  twitter: {
    card: "summary_large_image",
    images: [SOCIAL_IMAGE],
  },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f2f3f7" },
    { media: "(prefers-color-scheme: dark)", color: "#0e0f12" },
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
