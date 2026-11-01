# The Fly secrets for the app kuta as 1Password references, never values. `scripts/fly-secrets.sh fly.toml` resolves
# them with `op inject` and imports them (see README, Driftsättning). The item fly-kuta-new in the vault Development has one field
# per secret, named after the secret. Anything not secret lives in fly.toml under [env].
ConnectionStrings__Default=op://Development/fly-kuta-new/ConnectionStrings__Default
Supabase__SecretKey=op://Development/fly-kuta-new/Supabase__SecretKey
Storage__AccessKeyId=op://Development/fly-kuta-new/Storage__AccessKeyId
Storage__SecretAccessKey=op://Development/fly-kuta-new/Storage__SecretAccessKey
DataProtection__Certificate=op://Development/fly-kuta-new/DataProtection__Certificate
Garmin__ClientId=op://Development/fly-kuta-new/Garmin__ClientId
Garmin__ClientSecret=op://Development/fly-kuta-new/Garmin__ClientSecret
Turnstile__SecretKey=op://Development/fly-kuta-new/Turnstile__SecretKey
