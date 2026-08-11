#!/bin/zsh
# Post-migration verification for AddTillReleaseSettings.
#
# ⚠ VERIFIES THE COLUMNS, NOT `__EFMigrationsHistory`. A green history row proves a migration RAN,
# never that it did what its name says — on 2026-08-09 `AddDeviceSyncSignals`, named for three
# columns, contained only a CreateIndex, and every till lost its token because EF then built a
# SELECT against columns MySQL did not have. Runbook, EF migration section.
#
# ⚠ Reads the password from the same env file the backup uses and never echoes it. Do NOT run
# under `zsh -x`.
source ~/PLUTUS/secrets/mysql.env

/opt/homebrew/bin/mysql -u plutus -p"$MYSQL_PLUTUS_PASSWORD" --socket=/tmp/mysql.sock plutus -N -B -e "
SELECT CONCAT('column: ', COLUMN_NAME, ' ', COLUMN_TYPE, ' null=', IS_NULLABLE)
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='plutus' AND TABLE_NAME='TillReleaseSettings'
ORDER BY ORDINAL_POSITION;

SELECT CONCAT('migration: ', MigrationId)
FROM __EFMigrationsHistory
WHERE MigrationId LIKE '%AddTillReleaseSettings%';

SELECT CONCAT('rows in table: ', COUNT(*)) FROM TillReleaseSettings;
"
