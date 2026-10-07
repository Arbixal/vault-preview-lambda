import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv from 'ajv';
import addFormats from 'ajv-formats';
import { parse } from 'yaml';

const [responsePath, responseType] = process.argv.slice(2);
if (!responsePath || !responseType) {
  console.error('Usage: node validate-response.mjs <response-path> <response-type>');
  process.exit(2);
}

const fixturesDirectory = path.dirname(fileURLToPath(import.meta.url));
const openApi = parse(await readFile(path.resolve(fixturesDirectory, '..', 'openapi.yaml'), 'utf8'));
const response = JSON.parse(await readFile(responsePath, 'utf8'));
const contractId = 'https://contracts.invalid/vault-preview/v1/openapi.yaml';
const ajv = new Ajv({ allErrors: true, strict: false });
addFormats(ajv);

ajv.addSchema({
  $id: contractId,
  components: openApi.components
});

const validate = ajv.compile({
  $ref: `${contractId}#/components/schemas/${responseType}`
});

if (!validate(response)) {
  console.error(`${responseType}: ${ajv.errorsText(validate.errors)}`);
  process.exit(1);
}

console.log(`Validated generated ${responseType} response against ${contractId}.`);
