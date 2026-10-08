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

const requiredFixtures = new Map([
  ['app-config', 'AppConfigResponse'],
  ['current-season', 'CharacterProgressResponse'],
  ['empty-progress', 'CharacterProgressResponse'],
  ['partial-progress', 'CharacterProgressResponse'],
  ['unavailable-section', 'CharacterProgressResponse'],
  ['stale-metadata', 'CharacterProgressResponse'],
  ['unknown-activity-kind', 'CharacterProgressResponse'],
  ['future-season', 'CharacterProgressResponse'],
  ['legacy-flat-response-minimal', 'LegacyProgressResponse'],
  ['legacy-flat-response-full', 'LegacyProgressResponse']
]);

const fixturesByName = new Map(manifest.fixtures.map((fixture) => [fixture.name, fixture]));
assert.equal(fixturesByName.size, manifest.fixtures.length, 'fixture names must be unique');
for (const [name, responseType] of requiredFixtures) {
  const fixture = fixturesByName.get(name);
  assert.ok(fixture, `missing required fixture: ${name}`);
  assert.equal(fixture.responseType, responseType, `${name} has the wrong response type`);
}

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

const unknownActivity = JSON.parse(
  await readFile(path.join(fixturesDirectory, 'unknown-activity-kind.json'), 'utf8')
);
assert.equal(unknownActivity.sections[0].kind, 'future-account-activity');
assert.equal(unknownActivity.sections[0].status, 'future-status');
assert.equal(unknownActivity.sections[0].freshness, 'future-freshness');
assert.equal(unknownActivity.sections[0].slots[0].reward.rarity, 'future-rarity');

const futureSeason = JSON.parse(
  await readFile(path.join(fixturesDirectory, 'future-season.json'), 'utf8')
);
assert.equal(futureSeason.season.id, 'midnight-s3');
assert.ok(
  futureSeason.sections.some((section) => section.kind === 'world-events'),
  'future-season must include an additional activity kind'
);

const fullLegacy = JSON.parse(
  await readFile(path.join(fixturesDirectory, 'legacy-flat-response-full.json'), 'utf8')
);
const fullLegacyCharacter = fullLegacy['bixposter-nagrand'];
assert.equal(Object.keys(fullLegacyCharacter.delves).length, 11);
assert.ok(Object.keys(fullLegacyCharacter.raid).length >= 9);
assert.equal(fullLegacyCharacter.dungeons.length, 8);

console.log(`Validated ${manifest.fixtures.length} fixtures against ${manifest.contract}.`);
