# Fly-hemligheterna för appen kuta som 1Password-referenser, aldrig värden. `scripts/fly-secrets.sh fly.toml` löser upp
# dem med `op inject` och importerar dem (se README, Driftsättning). Databas-, Auth- och S3-hemligheter finns i fly-kuta-new;
# certifikatet ligger kvar i fly-kuta. Båda finns i valvet Development, med ett fält per hemlighet. Det som inte är hemligt ligger i fly.toml under [env].
ConnectionStrings__Default=op://Development/fly-kuta-new/ConnectionStrings__Default
Supabase__SecretKey=op://Development/fly-kuta-new/Supabase__SecretKey
Storage__AccessKeyId=op://Development/fly-kuta-new/Storage__AccessKeyId
Storage__SecretAccessKey=op://Development/fly-kuta-new/Storage__SecretAccessKey
DataProtection__Certificate=op://Development/fly-kuta/DataProtection__Certificate
