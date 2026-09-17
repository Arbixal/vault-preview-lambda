# Vault Preview Lambda

AWS Lambda functions that combine Blizzard World of Warcraft character data, Raider.IO Mythic+ data, and an S3-backed character cache.

All paths and commands in this document are relative to the repository root. Run the commands from a shell whose current directory is the root of this repository unless a command changes into a project directory.

## Requirements

- .NET SDK 10.0 or later
- AWS CLI v2 for provisioning AWS resources and local deployment
- An AWS account and region for the Lambda functions
- Amazon.Lambda.Tools 7.0 or later for deployment
- `zip` on Linux/macOS when creating deployment archives

Install or update the deployment tool with:

```bash
dotnet tool install -g Amazon.Lambda.Tools
dotnet tool update -g Amazon.Lambda.Tools
```

If the global tools directory is not already on `PATH`, add `$HOME/.dotnet/tools` to it before using `dotnet lambda`.

The projects target the AWS managed `dotnet10` Lambda runtime. The Lambda Annotations and AWS SDK package versions are kept current with the stable releases used by that runtime.

## Build

Run these commands from the repository root:

```bash
dotnet restore VaultPreviewLambda.sln
dotnet build VaultPreviewLambda.sln --configuration Release
```

Run the solution tests with:

```bash
dotnet test VaultPreviewLambda.sln --configuration Release
```

## API Contract

The canonical versioned API contract is `contracts/v1/openapi.yaml`. It defines the new `/v1/app-config` and `/v1/vault-progress/{region}/{realm}/{character}` endpoints, the deprecated legacy response boundary, structured errors, and the season/section/slot response model.

The API repository owns this schema. Shared fixtures and automated schema validation are tracked separately in T02.

## Functions

- `VaultPreviewLambda` exposes `GET /vault-progress/{region}/{realm}/{character}`.
- `CharacterDataLambda` refreshes cached delve statistics on the scheduled Tuesday trigger.
- `BlizzardTokenHandler` refreshes the Blizzard OAuth token stored in AWS Systems Manager Parameter Store.

The API stores Blizzard Journal metadata in the existing `vault-preview-data` S3 bucket under `journal-metadata/v1/`. Entries are fresh for 24 hours and may be served as stale last-known-good metadata for up to seven days when Blizzard is unavailable.

The functions expect these SSM parameters in the deployment region:

- `/Blizzard/ClientId`
- `/Blizzard/ClientSecret`
- `/Blizzard/Token`
- `/Blizzard/TokenExpires`

Season-aware Delve baseline refreshes also require the active revision provider environment values until the durable configuration endpoint is wired by T08:

- `VAULT_PREVIEW_SEASON_ID`
- `VAULT_PREVIEW_SEASON_REVISION`
- `VAULT_PREVIEW_SEASON_REVISION_HASH`
- Optional `VAULT_PREVIEW_SOURCE_SEASON_ID`

The API and scheduled-function SAM templates create Lambda execution roles with access limited to the `/Blizzard/*` parameter path and the cache bucket. The token function is deployed directly and therefore needs a separately created execution role.

## AWS Setup

The deployment workflow uses GitHub Actions OIDC, so it does not need long-lived AWS access keys in GitHub. Complete the following setup once for the AWS account and deployment region.

### 1. Choose the region and bucket names

Use one AWS region for the SSM parameters, Lambda functions, and deployment artifacts. The application cache currently uses the bucket name `vault-preview-data`; create that bucket or update both `src/Infrastructure.VaultCache/VaultCacheHandler.cs` and the two serverless templates before deploying. The Lambda Tools artifact bucket can have any globally unique name.

For local CLI deployment, select an AWS profile or another supported credential source and verify the account before creating resources:

```bash
export AWS_PROFILE="your-deployment-profile"
export AWS_REGION="your-aws-region"
aws sts get-caller-identity --region "$AWS_REGION"
```

Do not commit AWS credentials, client secrets, or other secret values to this repository.

### 2. Create the S3 buckets

Create a private cache bucket named `vault-preview-data` and a private build-artifact bucket. S3 bucket names are globally unique. Replace the build bucket value below with a name that is available in your account:

```bash
export LAMBDA_BUILD_BUCKET="your-unique-lambda-build-bucket"

aws s3api create-bucket \
  --bucket vault-preview-data \
  --region "$AWS_REGION" \
  --create-bucket-configuration LocationConstraint="$AWS_REGION"

aws s3api create-bucket \
  --bucket "$LAMBDA_BUILD_BUCKET" \
  --region "$AWS_REGION" \
  --create-bucket-configuration LocationConstraint="$AWS_REGION"
```

For `us-east-1`, omit `--create-bucket-configuration LocationConstraint="$AWS_REGION"`. If either bucket already exists in the selected account, keep it private and skip its `create-bucket` command. Block public access for both buckets.

### 3. Store Blizzard credentials in Parameter Store

Create API credentials in the Blizzard developer portal, then store them as `SecureString` parameters. The token and expiry parameters are initialized so the first token refresh can overwrite them:

```bash
export BLIZZARD_CLIENT_ID="your-blizzard-client-id"
export BLIZZARD_CLIENT_SECRET="your-blizzard-client-secret"

aws ssm put-parameter \
  --name /Blizzard/ClientId \
  --type SecureString \
  --value "$BLIZZARD_CLIENT_ID" \
  --overwrite \
  --region "$AWS_REGION"

aws ssm put-parameter \
  --name /Blizzard/ClientSecret \
  --type SecureString \
  --value "$BLIZZARD_CLIENT_SECRET" \
  --overwrite \
  --region "$AWS_REGION"

aws ssm put-parameter \
  --name /Blizzard/Token \
  --type SecureString \
  --value "not-initialized" \
  --overwrite \
  --region "$AWS_REGION"

aws ssm put-parameter \
  --name /Blizzard/TokenExpires \
  --type SecureString \
  --value "0" \
  --overwrite \
  --region "$AWS_REGION"
```

The token function replaces `/Blizzard/Token` and `/Blizzard/TokenExpires` after it obtains a token. If a customer-managed KMS key is used for the `SecureString` parameters, grant the Lambda execution roles `kms:Decrypt` for that key as well.

### 4. Create the token-function execution role

Create an IAM role trusted by `lambda.amazonaws.com`, attach `AWSLambdaBasicExecutionRole`, and add a policy equivalent to the following. Replace the region and account placeholders; do not put a real account ID or credential in the repository:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "ssm:GetParameter",
        "ssm:PutParameter"
      ],
      "Resource": "arn:aws:ssm:<region>:<account-id>:parameter/Blizzard/*"
    }
  ]
}
```

Record the role ARN as `BLIZZARD_TOKEN_ROLE_ARN`. The GitHub workflow passes this role to `deploy-function`; local deployment can pass it with the same option.

The two SAM deployments create their own execution roles from the policies in their templates. The deployment identity must be allowed to create and pass those roles.

### 5. Create the GitHub OIDC provider and deployment role

In IAM, add an OpenID Connect identity provider with:

- Provider URL: `https://token.actions.githubusercontent.com`
- Audience: `sts.amazonaws.com`

Create a dedicated deployment role whose trust policy is restricted to this repository and the `master` branch. Replace every placeholder in this example:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<account-id>:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
          "token.actions.githubusercontent.com:sub": "repo:<owner>/<repository>:ref:refs/heads/master"
        }
      }
    }
  ]
}
```

Attach a permissions policy to the deployment role that covers the resources deployed by this repository. At minimum it needs the following capability groups, scoped to the deployment region, build bucket, stack names, and execution roles wherever AWS supports resource-level restrictions:

- CloudFormation stack and change-set operations used by `deploy-serverless`, including create, update, describe, validate, execute, and delete operations.
- Lambda create, update, read, publish, tag, and permission operations used by both deployment commands.
- API Gateway and EventBridge rule operations used by the HTTP API and scheduled Lambda stack.
- S3 `GetBucketLocation`, `ListBucket`, `GetObject`, `PutObject`, and `DeleteObject` access to the Lambda Tools build bucket.
- IAM role read/create/update/delete operations for the SAM-generated execution roles, plus `iam:PassRole` for those roles and the token-function execution role.

Use IAM Access Analyzer or CloudTrail to reduce this initial deployment policy after the first successful deployment. The deployment role needs deployment permissions; it does not need permission to read the Blizzard parameters at runtime.

### 6. Configure GitHub Actions

In the repository's **Settings > Secrets and variables > Actions**, add:

- Secret `AWS_ROLE_TO_ASSUME`: the ARN of the GitHub OIDC deployment role.
- Variable `AWS_REGION`: the AWS deployment region.
- Variable `LAMBDA_BUILD_BUCKET`: the private S3 build-artifact bucket.
- Variable `BLIZZARD_TOKEN_ROLE_ARN`: the token-function Lambda execution role ARN.

The workflow already grants `id-token: write` and uses `aws-actions/configure-aws-credentials`. Do not add `AWS_ACCESS_KEY_ID` or `AWS_SECRET_ACCESS_KEY` GitHub secrets for this workflow. Restrict the IAM trust policy to the exact repository and branch, and protect the `master` branch or deployment environment as appropriate.

## Deployment

### Local deployment

After completing the AWS setup, run from the repository root. The variables must be set in the current shell:

```bash
dotnet restore VaultPreviewLambda.sln
dotnet build VaultPreviewLambda.sln --configuration Release

cd src/VaultPreviewLambda
dotnet lambda deploy-serverless vault-preview \
  --region "$AWS_REGION" \
  --s3-bucket "$LAMBDA_BUILD_BUCKET" \
  --configuration Release

cd ../CharacterDataLambda
dotnet lambda deploy-serverless character-data \
  --region "$AWS_REGION" \
  --s3-bucket "$LAMBDA_BUILD_BUCKET" \
  --configuration Release

cd ../BlizzardTokenHandler
dotnet lambda deploy-function BlizzardTokenHandler \
  --region "$AWS_REGION" \
  --function-role "$BLIZZARD_TOKEN_ROLE_ARN" \
  --configuration Release

cd ../..
```

On Windows, set the same variables in the command prompt and run the repository's deployment helper from the repository root:

```bat
set "AWS_REGION=your-aws-region"
set "LAMBDA_BUILD_BUCKET=your-unique-lambda-build-bucket"
set "BLIZZARD_TOKEN_ROLE_ARN=arn:aws:iam::<account-id>:role/<token-function-role>"
deploy.cmd
```

Deploy the token function before invoking the API or scheduled refresh so it can populate the cached Blizzard token. The two serverless commands create or update the `vault-preview` and `character-data` CloudFormation stacks.

### GitHub Actions deployment

`CI` builds the solution on pushes and pull requests targeting `master`. `Deploy Lambdas` builds and deploys all three functions on pushes to `master` or manual dispatch. A manual deployment must use the `master` ref when the trust policy is restricted as shown above.

## World Of Warcraft Data

The Blizzard integration uses the current regional profile and dynamic namespaces instead of always requesting US data. It supports the Blizzard region codes `us`, `eu`, `kr`, `tw`, and `cn`.

As of September 14, 2026, Blizzard's active season is Midnight Season 2. The raid progress mapping includes The Tidebound Grotto and The Venomous Abyss. Tidebound Grotto has one encounter:

- Nymrissa Wavecaller

The Venomous Abyss has eight encounters:

- Nek'zali the Soulcoiler
- Entombed Sentinels
- Vashnik the Malignant
- The Lost Explorers
- Sszorak
- The Twin Fangs
- The Coiled Altar
- Ula'tek

Midnight Season 1 data is also retained for The Voidspire and The Dreamrift. The Mythic+ season is read from Blizzard's dynamic API, and Raider.IO returns the current weekly run data.
