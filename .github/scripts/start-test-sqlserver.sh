#!/usr/bin/env bash
set -euo pipefail

# This password is generated for this disposable CI container only.
export MSSQL_SA_PASSWORD="Test-$(openssl rand -hex 24)-9aA!"
echo "::add-mask::$MSSQL_SA_PASSWORD"
test_connection="Server=localhost,1433;User ID=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=true"
echo "::add-mask::$test_connection"
echo "ACADEMIC_TEST_SQLSERVER=$test_connection" >> "$GITHUB_ENV"

docker run --detach --name academic-test-sqlserver \
    --env ACCEPT_EULA=Y --env MSSQL_SA_PASSWORD \
    --publish 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest

for attempt in {1..60}; do
    if docker exec --env SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" academic-test-sqlserver \
        /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q 'SELECT 1' -b >/dev/null 2>&1; then
        exit 0
    fi
    sleep 2
done

echo 'Test SQL Server did not become ready in time.' >&2
exit 1
