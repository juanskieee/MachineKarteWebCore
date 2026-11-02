-- Prepared by the 2026-11-02 source audit. NOT executed by that audit.
-- Review and run manually with a SELECT-only account on the intended server.
-- All data references explicitly target the original database.
-- Duplicate candidates are global: correlate them with the failing Form2
-- user/process/location/date range before attributing the error to a record.

SELECT
    SERVERPROPERTY('ServerName') AS ConnectedServer,
    DB_NAME() AS ConnectionDatabase,
    DB_ID(N'eMaintenanceSystemDB') AS TargetDatabaseId;

-- Normal Form2 modes correlate on (bmID, MachineNo).
-- TE Form2 correlates on bmID alone, including cards for different testers.
;WITH ActionCards AS
(
    SELECT 'PM' AS UserType, Seqno, bmID, MachineNo, DateCard, Incharge, MCStatus
    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard]
    UNION ALL
    SELECT 'CC', Seqno, bmID, MachineNo, DateCard, Incharge, MCStatus
    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_CC]
    UNION ALL
    SELECT 'EEE', Seqno, bmID, MachineNo, DateCard, Incharge, MCStatus
    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_EEE]
    UNION ALL
    SELECT 'VIS', Seqno, bmID, MachineNo, DateCard, Incharge, MCStatus
    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_VIS]
    UNION ALL
    SELECT 'TE', Seqno, bmID, MachineNo, DateCard, Incharge, MCStatus
    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE]
), CardMatches AS
(
    SELECT *,
        COUNT_BIG(*) OVER
        (
            PARTITION BY UserType, bmID,
                CASE WHEN UserType = 'TE' THEN NULL ELSE MachineNo END
        ) AS LegacyLookupMatchCount
    FROM ActionCards
    WHERE bmID IS NOT NULL
      AND (UserType = 'TE' OR MachineNo IS NOT NULL)
)
SELECT UserType, bmID, MachineNo, LegacyLookupMatchCount,
       Seqno, DateCard, Incharge, MCStatus
FROM CardMatches
WHERE LegacyLookupMatchCount > 1
ORDER BY UserType, bmID, MachineNo, Seqno;

-- Metadata visibility depends on the reviewing account's permissions.
-- No rows, or a NULL definition, is not proof that a trigger is absent.
SELECT
    s.name AS TableSchema,
    o.name AS TableName,
    t.name AS TriggerName,
    t.is_disabled AS IsDisabled,
    t.create_date AS TriggerCreated,
    t.modify_date AS TriggerModified,
    m.definition AS TriggerDefinition
FROM [eMaintenanceSystemDB].sys.triggers AS t
INNER JOIN [eMaintenanceSystemDB].sys.objects AS o ON o.object_id = t.parent_id
INNER JOIN [eMaintenanceSystemDB].sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN [eMaintenanceSystemDB].sys.sql_modules AS m ON m.object_id = t.object_id
WHERE s.name = N'dbo'
  AND o.name IN
  (
      N'MachineKarte_PCStatus', N'MachineKarte_ActionCard',
      N'MachineKarte_ActionCard_CC', N'MachineKarte_ActionCard_EEE',
      N'MachineKarte_ActionCard_VIS', N'MachineKarte_ActionCard_TE'
  )
ORDER BY o.name, t.name;
