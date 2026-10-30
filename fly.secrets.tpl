# Fly-hemligheterna för appen kuta som 1Password-referenser, aldrig värden. `scripts/fly-secrets.sh fly.toml` löser upp
# dem med `op inject` och importerar dem (se README, Driftsättning). Objektet fly-kuta i valvet Development har ett fält
# per hemlighet, med hemlighetens namn. Det som inte är hemligt ligger i fly.toml under [env].
ConnectionStrings__Default=op://Development/fly-kuta/ConnectionStrings__Default
Supabase__SecretKey=op://Development/fly-kuta/Supabase__SecretKey
DataProtection__Certificate=op://Development/fly-kuta/DataProtection__Certificate
