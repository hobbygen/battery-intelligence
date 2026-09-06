-- V002: time indexes on the aggregate tiers. See docs/database.md sections 4 and 7.
--
-- SampleMinute and SampleHour have PRIMARY KEY (BatteryId, <time>) and are
-- WITHOUT ROWID, so a history read that filters on the time column alone
-- (WHERE MinuteUtc BETWEEN ...), with no BatteryId, cannot use the primary key
-- and scans the whole table. The History page's minute/hour tier reads do
-- exactly that. These indexes turn those scans into range searches.
--
-- Purely additive (CREATE INDEX only) — no table rebuild, no row movement.

CREATE INDEX IF NOT EXISTS IX_SampleMinute_Time ON SampleMinute(MinuteUtc);
CREATE INDEX IF NOT EXISTS IX_SampleHour_Time   ON SampleHour(HourUtc);
