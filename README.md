# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Köra lokalt

Kräver Docker, Supabase CLI, .NET SDK och nvm (Node-versionen står i `.nvmrc`). `global.json` väljer .NET SDK 10.0.402 eller senare stabil patch i 10.0.4xx-serien, både lokalt och i Dockerbygget. Allt startas med:

```bash
scripts/run-dev.sh
```

Skriptet startar Supabase om det inte redan kör, bygger och startar API:t och kör webben med Vite (hot reload), med loggarna från båda i samma terminal. Ctrl+C stoppar API:t och webben; Supabase fortsätter köra tills `supabase stop`. API:t laddas inte om vid ändringar – starta om skriptet. Allt lokalt ligger på portar i 553xx-serien.

### Flera instanser samtidigt

```bash
scripts/run-dev.sh --ports 55400
```

Flyttar hela portserien: webben på BAS, API:t på BAS+2 och en egen Supabase-stack med egen databas på BAS+20–29 (Mailpit BAS+24). Konfigurationen och bygget hamnar i `.context/` (ignoreras av Git). Stoppa den instansens Supabase med `supabase stop --workdir .context/supabase-55400`. Testerna kör alltid mot standardinstansen.

### Adresser (standardinstansen)

| Vad | Adress |
|---|---|
| Webben | http://localhost:55300 |
| API | http://localhost:55302 |
| Mailpit (bekräftelsemejl) | http://127.0.0.1:55324 |
| Supabase Studio | http://127.0.0.1:55323 |

Kommer bekräftelsemejlet ändå på engelska med en länk till Supabase har auth-containern startat före Kong (skriptet hanterar det när det själv startar Supabase): `docker restart supabase_auth_kuta` och registrera igen.

### Lösenordsåterställning

Välj **Glömt lösenord?** på inloggningssidan. Mejlet finns i instansens Mailpit och länkar till `/api/auth/reset-password`. API:t verifierar länken och ger en separat krypterad HttpOnly-cookie som gäller i högst 15 minuter och bara för lösenordsbytet. Efter bytet avslutas kontots Kuta-sessioner och användaren loggar in med det nya lösenordet. Om anslutningen bryts under lösenordsbytet avslutas sessionerna också, eftersom bytet kan ha hunnit lyckas hos Supabase. Begär då en ny återställningslänk vid behov. Både begäran och lösenordsbytet kräver CSRF-token; ogiltiga, använda och utgångna länkar kräver en ny begäran.

Den lokala mejlmallen anges i `supabase/config.toml`. Om en redan körande miljö saknar återställningsmallen måste dess ägare stoppa och starta just den Supabase-instansen för att läsa in konfigurationen. Ändra inte en lånad instans.

Tester (kräver `supabase start`): `dotnet test --project api/Kuta.Api.Tests`.

## Driftsättning (Fly.io)

En egen Fly-app (`kuta`, region `arn`). `Dockerfile` bygger webben och API:t till en image där API:t servar webben, så att de delar origin. Migreringarna körs när appen startar.

**Domän före deploy:** `CanonicalHost` i `fly.toml` är `kutaapp.com`. Konfigurera DNS för domänen mot Fly-appen och verifiera att Fly har ett giltigt TLS-certifikat för `kutaapp.com` innan du deployar denna konfiguration. Appen skickar vidare `kuta.fly.dev` och `www.kutaapp.com` till `https://kutaapp.com`.

**Supabase-projektet** (en gång, i dashboarden):
- EU-region. Stäng av Data API. Minsta lösenordslängd 8.
- Authentication → URL Configuration: Site URL `https://kutaapp.com` (bekräftelselänken byggs på den). Adressen ska följa `CanonicalHost` i `fly.toml` och vara inställd före deploy.
- Authentication → Email Templates → Confirm signup: innehållet i `supabase/templates/confirmation.html`.
- Authentication → Email Templates → Reset password: innehållet i `supabase/templates/recovery.html`. Krävs före användning av lösenordsåterställning i drift; standardmallen skickar sessionstokens till webbläsaren i stället för vårt API.
- Egen SMTP (Authentication → SMTP). Supabases inbyggda mejl skickar bara till projektets egna medlemmar och några få per timme.

**Hemligheter** ligger i 1Password, aldrig i filer eller kommandon. [fly.secrets.tpl](fly.secrets.tpl) listar dem som `op://`-referenser; värdena finns i objektet `fly-kuta` i valvet `Development`, ett fält per hemlighet med hemlighetens namn:

| Fält | Värde |
|---|---|
| `ConnectionStrings__Default` | Supabase → Connect → Session pooler, i Npgsql-format: `Host=…;Port=5432;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Maximum Pool Size=10` |
| `Supabase__SecretKey` | Supabase → Project Settings → API Keys → secret key (`sb_secret_…`) |
| `DataProtection__Certificate` | Certifikatet som krypterar Data Protection-nycklarna, base64 på en rad (se nedan) |

Certifikatet skapas i en tillfällig katalog utanför repot och kopieras till urklipp; klistra in det i fältet, och töm sedan urklippet:

```bash
(cd "$(mktemp -d)" && openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 3650 -subj "/CN=kuta-dataprotection" -keyout key.pem -out cert.pem 2>/dev/null && openssl pkcs12 -export -inkey key.pem -in cert.pem -passout pass: | base64 | tr -d '\n' | pbcopy && rm -f key.pem cert.pem)
```

```bash
pbcopy < /dev/null
```

Förlorat certifikat förlorar ingen data, men alla måste logga in igen. Data Protection kontrollerar inte certifikatets utgångsdatum.

**Importera hemligheterna** (1Password CLI inloggad; värdena går från `op inject` till `fly secrets import` i minnet, och ingenting importeras om en referens saknas). Kör igen efter varje ändring i 1Password; en ny hemlighet läggs till i mallen och som fält samtidigt.

```bash
scripts/fly-secrets.sh fly.toml --stage
```

**Deploy:** kör en maskin tills migreringar och token-förnyelse klarar flera maskiner, enligt `AGENTS.md`.

```bash
fly deploy --ha=false
```
