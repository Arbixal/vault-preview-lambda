import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import Ajv from 'ajv';
import addFormats from 'ajv-formats';
import { parse } from 'yaml';

const fixturesDirectory = path.dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(await readFile(path.join(fixturesDirectory, 'manifest.json'), 'utf8'));
const openApi = parse(await readFile(path.resolve(fixturesDirectory, '..', 'openapi.yaml'), 'utf8'));
const contractId = 'https://contracts.invalid/vault-preview/v1/openapi.yaml';
const ajv = new Ajv({ allErrors: true, strict: false });
addFormats(ajv);

ajv.addSchema({
  $id: contractId,
  components: openApi.components
});

for (const fixture of manifest.fixtures) {
  const fixturePath = path.join(fixturesDirectory, fixture.path);
  const response = JSON.parse(await readFile(fixturePath, 'utf8'));
  const validate = ajv.compile({
    $ref: `${contractId}#/components/schemas/${fixture.responseType}`
  });

  assert.equal(validate(response), true, `${fixture.name}: ${ajv.errorsText(validate.errors)}`);
}

const currentSeason = JSON.parse(
  await readFile(path.join(fixturesDirectory, 'current-season.json'), 'utf8')
);
assert.equal(
  currentSeason.futureOptionalMetadata.source,
  'fixture-only-forward-compatibility-check',
  'current-season must exercise additive optional properties'
);
assert.equal(currentSeason.character.name, 'bixposter');

const fullLegacy = JSON.parse(
  await readFile(path.join(fixturesDirectory, 'legacy-flat-response-full.json'), 'utf8')
);
const fullLegacyCharacter = fullLegacy['bixposter-nagrand'];
assert.equal(Object.keys(fullLegacyCharacter.delves).length, 11);
assert.ok(Object.keys(fullLegacyCharacter.raid).length >= 9);
assert.equal(fullLegacyCharacter.dungeons.length, 8);

console.log(`Validated ${manifest.fixtures.length} fixtures against ${manifest.contract}.`);
