# Fly-hemligheterna för appen kuta som 1Password-referenser, aldrig värden. `scripts/fly-secrets.sh fly.toml` löser upp
# dem med `op inject` och importerar dem (se README, Driftsättning). Objektet fly-kuta-new i valvet Development har ett fält
# per hemlighet, med hemlighetens namn. Det som inte är hemligt ligger i fly.toml under [env].
ConnectionStrings__Default=op://Development/fly-kuta-new/ConnectionStrings__Default
Supabase__SecretKey=op://Development/fly-kuta-new/Supabase__SecretKey
Storage__AccessKeyId=op://Development/fly-kuta-new/Storage__AccessKeyId
Storage__SecretAccessKey=op://Development/fly-kuta-new/Storage__SecretAccessKey
DataProtection__Certificate=op://Development/fly-kuta-new/DataProtection__Certificate
Garmin__ClientId=op://Development/fly-kuta-new/Garmin__ClientId
Garmin__ClientSecret=op://Development/fly-kuta-new/Garmin__ClientSecret
