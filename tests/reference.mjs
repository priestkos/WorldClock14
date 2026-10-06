// Independent reference for the same grid the C# harness dumps, computed by ICU through Node's Intl -
// a completely different timezone implementation from .NET's. Any disagreement is a real finding.
//
//   node reference.mjs <out.tsv>
import { createWriteStream } from 'node:fs';

const ids = [
  'Asia/Kolkata', 'Asia/Tokyo', 'Asia/Seoul', 'Asia/Shanghai', 'Asia/Singapore', 'Asia/Kathmandu',
  'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'America/Sao_Paulo',
  'Europe/London', 'Europe/Berlin', 'Europe/Athens', 'Australia/Sydney', 'Australia/Adelaide',
  'Pacific/Auckland', 'Pacific/Honolulu',
];

const start = Date.UTC(2024, 0, 1);
const end = Date.UTC(2027, 0, 1);

const out = createWriteStream(process.argv[2]);
let buf = [];
const flush = () => { out.write(buf.join('')); buf = []; };

let rows = 0;
for (const id of ids) {
  const fmt = new Intl.DateTimeFormat('en-US', {
    timeZone: id,
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
    hourCycle: 'h23',
  });

  for (let t = start; t < end; t += 3600000) {
    const p = {};
    for (const part of fmt.formatToParts(t)) p[part.type] = part.value;

    // h23 should never hand back 24, but if it does the offset maths must not silently absorb it.
    if (p.hour === '24') {
      console.error(`unexpected hour 24 for ${id} at ${new Date(t).toISOString()}`);
      p.hour = '00';
    }

    const local = `${p.year}-${p.month}-${p.day} ${p.hour}:${p.minute}`;
    const asUtc = Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour, +p.minute);
    const offMinutes = Math.round((asUtc - t) / 60000);

    buf.push(`${id}\t${t / 1000}\t${local}\t${offMinutes}\n`);
    rows++;
    if (buf.length >= 20000) flush();
  }
}

flush();
out.end(() => console.error(`wrote ${rows} rows`));
