#!/usr/bin/env node

const requiredEnvironment = [
  "VAULT_PREVIEW_API_BASE_URL",
  "VAULT_PREVIEW_REGION",
  "VAULT_PREVIEW_REALM",
  "VAULT_PREVIEW_CHARACTER"
];

function fail(message) {
  throw new Error(message);
}

function requireString(value, description) {
  if (typeof value !== "string" || value.length === 0) {
    fail(`${description} is missing or empty.`);
  }
}

function assertString(value, description) {
  requireString(value, description);
  return value;
}

function assertExpected(value, environmentName, description) {
  const expected = process.env[environmentName];
  if (expected && value !== expected) {
    fail(`${description} '${value}' does not match ${environmentName} '${expected}'.`);
  }
}

async function getJson(baseUrl, path, headers) {
  const response = await fetch(`${baseUrl}${path}`, { headers });
  const text = await response.text();
  let body = null;
  if (text.length > 0) {
    try {
      body = JSON.parse(text);
    } catch {
      fail(`${path} returned non-JSON content with HTTP ${response.status}.`);
    }
  }

  if (!response.ok && response.status !== 304) {
    const errorCode = body?.error?.code;
    const suffix = errorCode ? ` (${errorCode})` : "";
    fail(`${path} returned HTTP ${response.status}${suffix}.`);
  }

  return { body, response };
}

async function main() {
  const missingEnvironment = requiredEnvironment.filter(
    (name) => !process.env[name]
  );
  if (missingEnvironment.length > 0) {
    fail(`Missing environment variables: ${missingEnvironment.join(", ")}.`);
  }

  const baseUrl = process.env.VAULT_PREVIEW_API_BASE_URL.replace(/\/+$/, "");
  const origin = process.env.VAULT_PREVIEW_SMOKE_ORIGIN || "https://vault-preview.bixnpieces.com";
  const requestHeaders = { Origin: origin };

  const appConfigResult = await getJson(baseUrl, "/v1/app-config", requestHeaders);
  if (appConfigResult.response.status !== 200) {
    fail(`/v1/app-config returned HTTP ${appConfigResult.response.status}; expected 200.`);
  }

  const appConfig = appConfigResult.body;
  if (appConfig?.schemaVersion !== 1) {
    fail("/v1/app-config did not return schemaVersion 1.");
  }
  const activeSeason = appConfig?.activeSeason;
  requireString(activeSeason?.id, "activeSeason.id");
  requireString(activeSeason?.revision, "activeSeason.revision");
  const revisionHash = assertString(activeSeason?.revisionHash, "activeSeason.revisionHash");
  if (!/^sha256:[0-9a-f]{64}$/.test(revisionHash)) {
    fail(`activeSeason.revisionHash '${revisionHash}' is not canonical.`);
  }

  assertExpected(activeSeason.id, "VAULT_PREVIEW_EXPECTED_SEASON_ID", "Active season ID");
  assertExpected(activeSeason.revision, "VAULT_PREVIEW_EXPECTED_REVISION_ID", "Active revision ID");
  assertExpected(revisionHash, "VAULT_PREVIEW_EXPECTED_REVISION_HASH", "Active revision hash");

  const configEtag = appConfigResult.response.headers.get("etag");
  if (configEtag !== `"${revisionHash}"`) {
    fail(`/v1/app-config returned unexpected ETag '${configEtag}'.`);
  }

  const revalidatedConfig = await getJson(
    baseUrl,
    "/v1/app-config",
    { ...requestHeaders, "If-None-Match": configEtag }
  );
  if (revalidatedConfig.response.status !== 304) {
    fail(`/v1/app-config conditional request returned HTTP ${revalidatedConfig.response.status}; expected 304.`);
  }

  const region = process.env.VAULT_PREVIEW_REGION;
  const realm = process.env.VAULT_PREVIEW_REALM;
  const character = process.env.VAULT_PREVIEW_CHARACTER;
  const progressPath = `/v1/vault-progress/${encodeURIComponent(region)}/${encodeURIComponent(realm)}/${encodeURIComponent(character)}`;
  const progressResult = await getJson(baseUrl, progressPath, requestHeaders);
  if (progressResult.response.status !== 200) {
    fail(`${progressPath} returned HTTP ${progressResult.response.status}; expected 200.`);
  }

  const progress = progressResult.body;
  if (progress?.schemaVersion !== 1) {
    fail(`${progressPath} did not return schemaVersion 1.`);
  }
  const progressSeason = progress?.season;
  requireString(progressSeason?.id, "progress.season.id");
  requireString(progressSeason?.revision, "progress.season.revision");
  requireString(progressSeason?.revisionHash, "progress.season.revisionHash");

  if (
    progressSeason.id !== activeSeason.id ||
    progressSeason.revision !== activeSeason.revision ||
    progressSeason.revisionHash !== activeSeason.revisionHash
  ) {
    fail("Character progress season metadata does not match /v1/app-config.");
  }

  console.log(
    `Smoke check passed: ${activeSeason.id}/${activeSeason.revision} ${revisionHash} for ${region}/${realm}/${character}.`
  );
}

try {
  await main();
} catch (error) {
  console.error(`Smoke check failed: ${error.message}`);
  process.exitCode = 1;
}
