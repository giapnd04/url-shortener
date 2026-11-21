IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    CREATE TABLE [api_clients] (
        [id] int NOT NULL IDENTITY,
        [name] nvarchar(100) NOT NULL,
        [api_key_hash] binary(32) NOT NULL,
        [created_at] datetime2 NOT NULL,
        [disabled_at] datetime2 NULL,
        CONSTRAINT [PK_api_clients] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    CREATE TABLE [short_links] (
        [id] bigint NOT NULL IDENTITY,
        [code] varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [target_url] nvarchar(2048) NOT NULL,
        [client_id] int NOT NULL,
        [created_at] datetime2 NOT NULL,
        [updated_at] datetime2 NOT NULL,
        [expires_at] datetime2 NULL,
        [disabled_at] datetime2 NULL,
        CONSTRAINT [PK_short_links] PRIMARY KEY ([id]),
        CONSTRAINT [FK_short_links_api_clients_client_id] FOREIGN KEY ([client_id]) REFERENCES [api_clients] ([id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_api_clients_api_key_hash] ON [api_clients] ([api_key_hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_short_links_client_id_created_at] ON [short_links] ([client_id], [created_at]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_short_links_code] ON [short_links] ([code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261119054741_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261119054741_InitialCreate', N'9.0.20');
END;

COMMIT;
GO

