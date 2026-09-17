@echo off
setlocal

if "%LAMBDA_BUILD_BUCKET%"=="" (
    echo LAMBDA_BUILD_BUCKET is not set.
    exit /b 1
)

if "%BLIZZARD_TOKEN_ROLE_ARN%"=="" (
    echo BLIZZARD_TOKEN_ROLE_ARN is not set.
    exit /b 1
)

if "%AWS_REGION%"=="" (
    echo AWS_REGION is not set.
    exit /b 1
)

if "%VAULT_PREVIEW_SEASON_ID%"=="" (
    echo VAULT_PREVIEW_SEASON_ID is not set.
    exit /b 1
)

if "%VAULT_PREVIEW_SEASON_REVISION%"=="" (
    echo VAULT_PREVIEW_SEASON_REVISION is not set.
    exit /b 1
)

if "%VAULT_PREVIEW_SEASON_REVISION_HASH%"=="" (
    echo VAULT_PREVIEW_SEASON_REVISION_HASH is not set.
    exit /b 1
)

pushd "%~dp0src\BlizzardTokenHandler"
dotnet lambda deploy-function BlizzardTokenHandler --region "%AWS_REGION%" --function-role "%BLIZZARD_TOKEN_ROLE_ARN%"
if errorlevel 1 exit /b 1

popd
pushd "%~dp0src\VaultPreviewLambda"
dotnet lambda deploy-serverless vault-preview --region "%AWS_REGION%" --s3-bucket "%LAMBDA_BUILD_BUCKET%" --template-parameters "VaultPreviewSeasonId=%VAULT_PREVIEW_SEASON_ID%;VaultPreviewSeasonRevision=%VAULT_PREVIEW_SEASON_REVISION%;VaultPreviewSeasonRevisionHash=%VAULT_PREVIEW_SEASON_REVISION_HASH%;VaultPreviewSourceSeasonId=%VAULT_PREVIEW_SOURCE_SEASON_ID%"
if errorlevel 1 exit /b 1

popd
pushd "%~dp0src\CharacterDataLambda"
dotnet lambda deploy-serverless character-data --region "%AWS_REGION%" --s3-bucket "%LAMBDA_BUILD_BUCKET%" --template-parameters "VaultPreviewSeasonId=%VAULT_PREVIEW_SEASON_ID%;VaultPreviewSeasonRevision=%VAULT_PREVIEW_SEASON_REVISION%;VaultPreviewSeasonRevisionHash=%VAULT_PREVIEW_SEASON_REVISION_HASH%;VaultPreviewSourceSeasonId=%VAULT_PREVIEW_SOURCE_SEASON_ID%"
if errorlevel 1 exit /b 1

popd
endlocal
