// Builds the platform icon set. Run via yarn node build-icons.mjs
//
// Uses sharp (libvips + librsvg) because it is the one SVG rasteriser that is reliably available as a
// prebuilt binary; .icns comes from macOS's iconutil and .ico is assembled here (a modern ICO is just a
// directory of PNG payloads, so it needs no extra dependency).
import { execFileSync } from "node:child_process";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const here = dirname(fileURLToPath(import.meta.url));
const repo = join(here, "..", "..");
const appAssets = join(repo, "src", "Noto.App", "Assets");
const desktopAssets = join(repo, "src", "Noto.Desktop", "Assets");
const tmp = join(repo, "dist", "iconset");

// --- 1. GENERATE VECTOR DESIGNS ---

const appSvg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <defs>
    <!-- Bluish Gradient (Cyan to Cobalt to Midnight Blue) -->
    <linearGradient id="bg-grad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#06B6D4" />
      <stop offset="50%" stop-color="#3B82F6" />
      <stop offset="100%" stop-color="#1E3A8A" />
    </linearGradient>
  </defs>
  <rect width="1024" height="1024" rx="230" fill="url(#bg-grad)" />
  <g>
    <!-- Hardcoded drop-shadow for bulletproof sharp/librsvg rendering -->
    <path d="M 270,680 L 270,380 L 490,680 L 760,330" 
          fill="none" stroke="#000000" stroke-opacity="0.25" stroke-width="120" 
          stroke-linecap="round" stroke-linejoin="round" transform="translate(0, 16)" />
    <!-- Crisp white foreground stroke -->
    <path d="M 270,680 L 270,380 L 490,680 L 760,330" 
          fill="none" stroke="#FFFFFF" stroke-width="120" 
          stroke-linecap="round" stroke-linejoin="round" />
  </g>
</svg>`;

const traySvg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="32" height="32">
  <path d="M 6,24 L 6,10 L 14,24 L 26,8" 
        fill="none" stroke="#000000" stroke-width="4" 
        stroke-linecap="round" stroke-linejoin="round" />
</svg>`;

// Write source SVGs to the current directory (assets/icon/)
writeFileSync(join(here, "noto.svg"), appSvg.trim());
writeFileSync(join(here, "noto-tray.svg"), traySvg.trim());

// We can bypass readFileSync and just load the strings into Buffers directly for Sharp
const app = Buffer.from(appSvg.trim());
const tray = Buffer.from(traySvg.trim());

// --- 2. RENDER PLATFORM ASSETS ---

const render = (svg, size) =>
  sharp(svg, { density: (72 * size) / 1024 })
    .resize(size, size)
    .png({ compressionLevel: 9 })
    .toBuffer();

/** A modern .ico is a 6-byte header, a 16-byte directory entry per image, then the PNG payloads. */
function ico(images) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  const dir = Buffer.alloc(16 * images.length);
  let offset = header.length + dir.length;
  images.forEach(({ size, png }, i) => {
    const at = 16 * i;
    const dim = size >= 256 ? 0 : size; // 0 means 256 in the ICO format
    dir.writeUInt8(dim, at);
    dir.writeUInt8(dim, at + 1);
    dir.writeUInt16LE(1, at + 4); // colour planes
    dir.writeUInt16LE(32, at + 6); // bits per pixel
    dir.writeUInt32LE(png.length, at + 8);
    dir.writeUInt32LE(offset, at + 12);
    offset += png.length;
  });
  return Buffer.concat([header, dir, ...images.map((i) => i.png)]);
}

mkdirSync(appAssets, { recursive: true });
mkdirSync(desktopAssets, { recursive: true });
rmSync(tmp, { recursive: true, force: true });
mkdirSync(tmp, { recursive: true });

// .icns: iconutil wants an .iconset with these exact names, sizes 16..1024.
const iconset = join(tmp, "noto.iconset");
mkdirSync(iconset, { recursive: true });
for (const size of [16, 32, 128, 256, 512]) {
  for (const scale of [1, 2]) {
    const px = size * scale;
    const name =
      scale === 1 ? `icon_${size}x${size}.png` : `icon_${size}x${size}@2x.png`;
    writeFileSync(join(iconset, name), await render(app, px));
  }
}
execFileSync("iconutil", [
  "-c",
  "icns",
  iconset,
  "-o",
  join(desktopAssets, "noto.icns"),
]);

// .ico for the Windows executable icon.
const icoSizes = [16, 24, 32, 48, 64, 128, 256];
writeFileSync(
  join(desktopAssets, "noto.ico"),
  ico(
    await Promise.all(
      icoSizes.map(async (size) => ({ size, png: await render(app, size) })),
    ),
  ),
);

// Window icon (Linux/Windows taskbar, and what Avalonia shows in the window itself).
writeFileSync(join(appAssets, "noto.png"), await render(app, 512));

// Menu-bar glyph: 16/32pt at 1x and 2x, monochrome so macOS can invert it.
for (const px of [16, 32, 64]) {
  const name = px === 16 ? "tray.png" : `tray@${px / 16}x.png`;
  writeFileSync(join(desktopAssets, name), await render(tray, px));
}

rmSync(tmp, { recursive: true, force: true });
console.log(
  [
    "icons built:",
    "  assets/icon/noto.svg                   (Source Vector)",
    "  assets/icon/noto-tray.svg              (Source Vector)",
    "  src/Noto.App/Assets/noto.png           512",
    "  src/Noto.Desktop/Assets/noto.icns      16..1024",
    `  src/Noto.Desktop/Assets/noto.ico       ${icoSizes.join(", ")}`,
    "  src/Noto.Desktop/Assets/tray.png       16 (+32, 64 for the 2x menu bar)",
  ].join("\n"),
);
