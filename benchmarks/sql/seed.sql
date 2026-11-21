-- Deterministic benchmark dataset (US-04): 100,000 short links owned by one "bench" client.
--
-- The code of seed index n (1..100000) is Base62(n * 1000000007 mod 62^7), left-padded to 7 chars.
-- 1000000007 shares no factor with 62^7 (= 2^7 * 31^7), so n -> code is a bijection: codes are unique
-- and every run produces exactly the same dataset with no random seed or data file.
-- benchmarks/redirect.js computes the same function, so k6 knows which codes exist (n <= 100000)
-- and which do not (n > 100000).
--
-- Idempotent: does nothing if the bench client already owns 100,000 links.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @linkCount int = 100000;
DECLARE @alphabet char(62) = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';
DECLARE @mod bigint = 3521614606208;      -- 62^7
DECLARE @mult bigint = 1000000007;
DECLARE @now datetime2 = SYSUTCDATETIME();
-- SHA-256 of the bench API key "bench-key-not-secret"; only used to own the rows, never to call the API.
DECLARE @benchKeyHash binary(32) = HASHBYTES('SHA2_256', CAST('bench-key-not-secret' AS varchar(100)));

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM api_clients WHERE api_key_hash = @benchKeyHash)
    INSERT INTO api_clients (name, api_key_hash, created_at) VALUES ('bench', @benchKeyHash, @now);

DECLARE @clientId int = (SELECT id FROM api_clients WHERE api_key_hash = @benchKeyHash);

IF (SELECT COUNT(*) FROM short_links WHERE client_id = @clientId) < @linkCount
BEGIN
    DELETE FROM short_links WHERE client_id = @clientId;

    WITH numbers AS (
        SELECT TOP (@linkCount) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS bigint) AS n
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    ),
    scrambled AS (
        SELECT n, (n * @mult) % @mod AS x FROM numbers
    )
    INSERT INTO short_links (code, target_url, client_id, created_at, updated_at, expires_at, disabled_at)
    SELECT
        SUBSTRING(@alphabet, CAST(x / 56800235584 % 62 AS int) + 1, 1)   -- 62^6
      + SUBSTRING(@alphabet, CAST(x / 916132832 % 62 AS int) + 1, 1)     -- 62^5
      + SUBSTRING(@alphabet, CAST(x / 14776336 % 62 AS int) + 1, 1)      -- 62^4
      + SUBSTRING(@alphabet, CAST(x / 238328 % 62 AS int) + 1, 1)        -- 62^3
      + SUBSTRING(@alphabet, CAST(x / 3844 % 62 AS int) + 1, 1)          -- 62^2
      + SUBSTRING(@alphabet, CAST(x / 62 % 62 AS int) + 1, 1)
      + SUBSTRING(@alphabet, CAST(x % 62 AS int) + 1, 1),
        CONCAT('https://example.com/bench/', n),
        @clientId, @now, @now, NULL, NULL
    FROM scrambled;
END

COMMIT TRANSACTION;

-- Fresh statistics so the plan does not depend on when auto-update kicks in.
UPDATE STATISTICS short_links WITH FULLSCAN;

SELECT COUNT(*) AS bench_links FROM short_links WHERE client_id = @clientId;
