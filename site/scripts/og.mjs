// Renders public/og.png (1200×630) from an inline SVG with sharp, which Astro already ships.
// Run with: node scripts/og.mjs
import { writeFile } from 'node:fs/promises';
import sharp from 'sharp';

const W = 1200;
const H = 630;

function arc(cx, cy, r, fraction, stroke, color) {
  const rad = (d) => (d * Math.PI) / 180;
  const sweep = 240 * fraction;
  const start = { x: cx + r * Math.cos(rad(210)), y: cy - r * Math.sin(rad(210)) };
  const endAngle = 210 - sweep;
  const end = { x: cx + r * Math.cos(rad(endAngle)), y: cy - r * Math.sin(rad(endAngle)) };
  const large = sweep > 180 ? 1 : 0;
  return `<path d="M ${start.x} ${start.y} A ${r} ${r} 0 ${large} 1 ${end.x} ${end.y}" fill="none" stroke="${color}" stroke-width="${stroke}" stroke-linecap="round"/>`;
}

const dials = [
  { x: 860, y: 315, r: 150, f: 0.41, color: '#34d399', stroke: 26 },
  { x: 1090, y: 470, r: 60, f: 0.87, color: '#fb7185', stroke: 12 },
  { x: 1060, y: 150, r: 46, f: 0.63, color: '#fbbf24', stroke: 10 }
];

const svg = `
<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">
  <defs>
    <radialGradient id="g" cx="0.7" cy="0.5" r="0.7">
      <stop offset="0" stop-color="#10b981" stop-opacity="0.18"/>
      <stop offset="1" stop-color="#0b0e14" stop-opacity="0"/>
    </radialGradient>
  </defs>
  <rect width="${W}" height="${H}" fill="#0b0e14"/>
  <rect width="${W}" height="${H}" fill="url(#g)"/>
  ${dials.map((d) => arc(d.x, d.y, d.r, 1, d.stroke, 'rgba(255,255,255,0.10)') + arc(d.x, d.y, d.r, d.f, d.stroke, d.color)).join('')}
  <text x="860" y="340" text-anchor="middle" font-family="Segoe UI, Inter, Helvetica, Arial, sans-serif" font-size="64" font-weight="600" fill="#f1f5f9">41%</text>
  <g font-family="Segoe UI, Inter, Helvetica, Arial, sans-serif">
    <rect x="72" y="76" width="44" height="44" rx="11" fill="#171c26"/>
    ${arc(94, 98, 13, 1, 5, '#384358')}${arc(94, 98, 13, 0.66, 5, '#34d399')}
    <text x="130" y="109" font-size="30" font-weight="600" fill="#f1f5f9">Tokendial</text>
    <text x="72" y="270" font-size="58" font-weight="600" fill="#f1f5f9">Know how much of your</text>
    <text x="72" y="340" font-size="58" font-weight="600" fill="#f1f5f9">AI coding limits</text>
    <text x="72" y="410" font-size="58" font-weight="600" fill="#34d399">you have left.</text>
    <text x="72" y="490" font-size="24" fill="#94a3b8">Native dials for Claude Code, Codex, Copilot, Cursor and more.</text>
    <text x="72" y="528" font-size="24" fill="#94a3b8">macOS · Windows · free and open source</text>
  </g>
</svg>`;

const png = await sharp(Buffer.from(svg), { density: 96 }).png({ compressionLevel: 9 }).toBuffer();
await writeFile(new URL('../public/og.png', import.meta.url), png);
console.log(`og.png ${png.length} bytes`);
