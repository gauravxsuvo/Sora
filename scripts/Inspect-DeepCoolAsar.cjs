// Static archive inspection only. No vendor module, bytecode or executable is loaded.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const workspace = path.resolve(__dirname, '..');
const asar = require(path.join(workspace, 'tools/node_modules/@electron/asar/lib/asar.js'));
const input = path.resolve(process.argv[2] || 'C:/DeepCool/resources/app.asar');
if (!process.argv[3]) throw new Error('Supply a new evidence output directory.');
const output = path.resolve(process.argv[3]);
if (fs.existsSync(output)) throw new Error('Output already exists; preserve the previous investigation.');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
async function main() {
  fs.mkdirSync(output, { recursive: true });
  const old = JSON.parse(fs.readFileSync(path.join(workspace, 'deepcool-re/evidence/extraction-manifest.json'), 'utf8'));
  const previous = new Map(old.files.map(file => [file.path, file]));
  const inventory = asar.listPackage(input);
  fs.writeFileSync(path.join(output, 'asar-files.json'), JSON.stringify(inventory, null, 2));
  const files = [];
  for (const member of inventory) {
    const logical = member.replace(/^[/\\]+/, '').replaceAll('\\', '/');
    if (logical !== 'package.json' && !(logical.startsWith('out/') && /\.(js|jsc|json)$/.test(logical))) continue;
    const destination = path.resolve(output, 'recovered', logical);
    if (!destination.startsWith(path.join(output, 'recovered') + path.sep)) throw new Error('Archive path leaves evidence directory.');
    const bytes = asar.extractFile(input, member.replace(/^[/\\]+/, ''));
    const sha256 = hash(bytes);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, bytes);
    files.push({ path: logical, length: bytes.length, sha256, unchangedFromPrevious: previous.get(logical)?.sha256 === sha256 });
  }
  const archiveHash = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(input)) archiveHash.update(chunk);
  const pkg = JSON.parse(fs.readFileSync(path.join(output, 'recovered/package.json'), 'utf8'));
  const report = {
    source: input, sourceSha256: archiveHash.digest('hex'), packageVersion: pkg.version,
    electron: pkg.devDependencies?.electron || pkg.dependencies?.electron || null,
    priorSourceSha256: old.source_sha256, vendorCodeExecuted: false,
    inventoryEntries: inventory.length, extractedFiles: files.length,
    unchangedFiles: files.filter(file => file.unchangedFromPrevious).length,
    mainFiles: files.filter(file => file.path.startsWith('out/main/')), files
  };
  fs.writeFileSync(path.join(output, 'comparison.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify({ ...report, files: undefined }, null, 2));
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
