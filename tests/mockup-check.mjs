// Runs preview.html's own inline script against a stubbed DOM, so the mockup is verified rather than
// assumed (browser sessions on this machine block file:// URLs).
//
//   node mockup-check.mjs ../preview.html
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const html = readFileSync(process.argv[2], 'utf8');
const blocks = [...html.matchAll(/<script(?![^>]*src=)[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
if (blocks.length !== 1) {
  console.error(`expected exactly 1 inline script, found ${blocks.length}`);
  process.exit(1);
}
const script = blocks[0];

const elements = {};
const el = (id) => {
  if (!elements[id]) {
    elements[id] = { id, textContent: '', innerHTML: '', style: {}, listeners: {}, addEventListener(ev, fn) { this.listeners[ev] = fn; } };
  }
  return elements[id];
};

const context = {
  document: { getElementById: el, querySelectorAll: () => [], title: 'mockup' },
  navigator: { clipboard: { writeText: () => {} } },
  setInterval: () => 0,   // the live tick is not needed; we inspect the first render
  Intl,
  Date,
  Math,
  console,
};
context.globalThis = context;

vm.createContext(context);
vm.runInContext(script, context, { filename: 'preview.html' });

const line = el('flashText').textContent;
const rows = [...el('rows').innerHTML.matchAll(/class="zone[^"]*">([^<]+)<\/td>\s*<td><span class="time">([^<]+)<\/span>/g)]
  .map((m) => ({ zone: m[1], time: m[2] }));

console.log('zone rows rendered :', rows.length);
for (const r of rows) console.log(`   ${r.zone.padEnd(4)} ${r.time}`);
console.log('copied line        :', line);
console.log('base echo          :', el('echo').textContent);
console.log('preview header     :', el('previewHead').textContent);
console.log('ET offset shown    :', el('etOffset').textContent);
console.log('local zone resolved:', el('localName').textContent);
console.log('base zone marked   :', /class="zone base"/.test(el('rows').innerHTML) ? 'yes' : 'no');

let failures = 0;
const expect = (what, ok) => { if (!ok) { failures++; console.log(`   FAIL ${what}`); } else { console.log(`   ok   ${what}`); } };

console.log('assertions:');
expect('four zone rows', rows.length === 4);
expect('row order is IST, JST, UTC, ET', rows.map((r) => r.zone).join(',') === 'IST,JST,UTC,ET');
expect('every row has a HH:MM time', rows.every((r) => /^\d{2}:\d{2}$/.test(r.time)));
expect('copied line is a 4-zone pipe list', /^\d{2}:\d{2} IST \| \d{2}:\d{2} JST \| \d{2}:\d{2} UTC \| \d{2}:\d{2} ET(-?\d+d)?$/.test(line));
expect('ET offset column is populated', /^[+-]\d{2}:\d{2}$/.test(el('etOffset').textContent));
expect('local zone name came from Intl', el('localName').textContent.length > 0);
expect('a clicking copy handler was attached', typeof elements.copyAll?.listeners.click === 'function');

console.log(failures === 0 ? '\nmockup script: OK' : `\nmockup script: ${failures} FAILURES`);
process.exit(failures === 0 ? 0 : 1);
