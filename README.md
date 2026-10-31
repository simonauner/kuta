# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Publik startsida och app

`/` är en statisk, svenskspråkig startsida (`web/index.html`, stilar i `web/src/landing.css`). Fotot i `web/public/images/forest-trail.webp` tillhör Simon Auner och serveras lokalt som WebP, utan beroende av Stravas bildserver; produktillustrationerna är designskisser med syntetiska exempeldata. Startsidan och appen delar färgvariabler i `web/src/theme.css` och följer systemets ljusa eller mörka tema med neutrala ytor och orange accent. Innehåll och SEO-metadata finns i HTML utan JavaScript. `robots.txt` och `sitemap.xml` finns i `web/public/`; canonical och sitemap använder `https://kutaapp.com/`.

Logotypen ”Spåret” finns som fristående SVG i `web/public/brand/` (mörk och ljus variant). Bokstäverna är vektorkonturer och kräver ingen font. Symbolen i `web/public/favicon.svg` används också av PWA-manifestet. PNG-ikoner för PWA finns i `web/public/icons/` (192/512 samt maskable 512 med extra marginal). `web/public/apple-touch-icon.png` (180×180, heltäckande bakgrund) används av iOS, och `favicon-32.png` är PNG-fallback för webbläsare. Båda HTML-dokumenten länkar till ikonerna; PWA:n startar på `/app`. SVG-underlagen för hemskärmsikonerna finns i `web/public/brand/app-icon.svg` och `app-icon-maskable.svg`.

React-appen öppnas på `/app`, med vyerna `/app/activities`, `/app/stats` samt `/app/profile` och `/app/settings`. `web/app.html` har `noindex`. Mejlets bekräftelselänk återvänder till `/app`. Vite bygger båda HTML-filerna, och .NET serverar dem från samma origin. PWA:n startar på `/app` och använder bara appskalet som navigeringsfallback inom `/app`.

Profil visar löparens namn och länkar till Utrustning på `/app/profile/equipment`. Kugghjulet öppnar Inställningar på `/app/settings`, med språkval, pulssamtycke, Strava-import, e-postadress, utloggning och Danger zone för att radera alla pass. Vanlig FIT-uppladdning och manuell registrering nås via Pass → Skapa ny aktivitet. Pulssamtycket kan ändras med samma checkbox både i filmodalen och under Inställningar. Checkboxen visas även när samtycket redan är givet.

## Språk och URL:er

React-appen använder `react-i18next` med svenska och engelska. Språkväljaren finns vid inloggning och under Inställningar i appen. Valet sparas lokalt i webbläsaren (`kuta.language`); utan sparat val prioriteras svenskt IP, sedan det första stödda språket i webbläsarens språklista, annars engelska. Bara uttryckliga val sparas. API:t bedömer svenska IPv4-/IPv6-nät med en lokal lista, utan externa IP-uppslag; källan och hur listan uppdateras finns i [Localization/README.md](api/Kuta.Api/Localization/README.md). Om uppslaget misslyckas eller tar mer än 1,5 sekunder används webbläsarens språk. Valet uppdaterar även HTML-dokumentets `lang`. Det synkas inte mellan enheter.

Appens URL:er är `/app` (flöde), `/app/activities`, `/app/stats`, `/app/profile` och `/app/settings` på båda språken.

Översättningar finns i `web/src/locales/sv.json` och `en.json`; lägg till samma nycklar i båda. API-felkoder översätts i webben, med ett generellt fel för okända koder. Mejlmallar och PWA-manifest är tills vidare svenska. Språkval och katalogernas täckning kontrolleras med `cd web && npm test` (Node enligt `.nvmrc`).

## Passlistans sidindelning

`GET /api/activities?timeZone=Europe%2FStockholm` ger `{ items, nextCursor }`, högst 25 pass
sorterade på `startedAt` och `id` fallande. Nästa sida hämtas med `beforeStartedAt` och `beforeId`
från `nextCursor`; null betyder att historiken är slut. Utelämnad tidszon ger UTC, webben skickar
webbläsarens IANA-tidszon. Listans `showMonth` och `showWeek` anger om fler egna pass finns i
samma lokala år respektive månad, även utanför sidan. Därmed ändras inte rubriknivåerna när
”Visa äldre pass” fyller på listan. Inga tidsserier eller rutter ingår i listsvaren.

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

Flyttar hela portserien: webben på BAS, API:t på BAS+2 och en egen Supabase-stack med egen databas på BAS+20–29 (Mailpit BAS+24). Konfigurationen och bygget hamnar i `.context/` (ignoreras av Git). Stoppa den instansens Supabase med `supabase stop --workdir .context/supabase-55400`. Kör testerna mot den med `KUTA_PORTS=55400 dotnet test --project api/Kuta.Api.Tests`.

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

Tester (kräver `supabase start`, med Storage påslaget): `dotnet test --project api/Kuta.Api.Tests`, eller `KUTA_PORTS=BAS dotnet test …` mot en egen instans.

API-jobbet i GitHub Actions använder `KUTA_PORTS=18000` och flyttar Supabase-konfigurationens portar i jobbets checkout. Portarna 18020–18029 ligger utanför Linux normala intervall för tillfälliga utgående anslutningar, så nedladdning av Docker-images inte kan ta databasporten före start. Lokal standardkonfiguration ligger kvar på 553xx.

Frontendkontroller med Node-versionen i `.nvmrc`: kör `npm ci` i `web/`, sedan
`npm run format:check`, `npm run lint`, `npm test` och `npm run build`.
`npm run format` formaterar webbfilerna med Prettier. API-klientens tester använder
Nodes inbyggda testverktyg och mockade nätverksanrop; de kräver inte Supabase.
GitHub Actions kör frontendkontrollerna vid ändringar i `web/` och bygger API:t och
testprojektet vid ändringar i `api/` eller `supabase/`, på pull requests och push till
`main`. Respektive workflow och verktygskonfiguration (`.nvmrc` respektive
`global.json`) startar också motsvarande kontroller. `Dockerfile` och `.dockerignore`
startar båda. Ändringar i enbart `.md`-filer startar inget av dessa workflows, även
under `web/` eller `api/`. Backendtester mot lokal Supabase körs fortfarande lokalt
enligt ovan; backend-workflowet kontrollerar enbart att projekten bygger.

API-jobbet i `.github/workflows/api.yml` kör hela `api/Kuta.Api.Tests` vid ändringar
i API:t, Supabase-konfigurationen, `global.json`, `Dockerfile` eller själva jobbet,
både på pull requests och push till `main`. Det installerar .NET enligt `global.json`
och Supabase CLI 2.72.7, startar en tillfällig lokal Supabase med Auth, Mailpit och
Storage, läser dess lokala nycklar och väntar på auth efter omstart innan testerna
körs. Testerna körs sekventiellt (`-- --parallel none`) så att testservrarna inte
försöker migrera den tomma databasen samtidigt. Inga produktionshemligheter
eller hostade tjänster används. Supabase stoppas även om testerna misslyckas.

Footern visar copyright och webbens byggnummer (`ÅÅÅÅMMDD.ttmmss`, UTC), som sätts
automatiskt av Vite vid bygget, även i Docker. I utvecklingsläge visas `Version utveckling`.

## Utrustning och användning

Lägg till utrustning i modalen under Profil → Utrustning och koppla den till manuella pass eller FIT-importer. Skor kan även skapas direkt med ”Skapa ny” vid skofältet; den nya skon väljs automatiskt utan att aktivitetsutkastet försvinner.
Kopplingarna kan också ändras på passsidan. Ett pass kan använda flera
utrustningsdelar. Skor (`shoe`) är den första typen; nya typer läggs till i
`EquipmentEndpoints.Kinds` och i båda språkfilernas `equipmentKinds`. Typväljaren i formuläret visas först när fler än en typ finns.

Migrering 009 gör om `shoes` till `equipment` och bevarar befintliga skor,
startdistanser och passkopplingar. `activity_equipment` kopplar pass och utrustning;
sammansatta främmande nycklar kräver samma ägare på båda sidor.

`distanceM` och `durationS` är startvärdena plus summan av de kopplade passens
`distance_m` och `duration_s` (förfluten tid inklusive pauser). Samma pass räknas
högst en gång per utrustning. Totalerna beräknas vid läsning, så ändrade kopplingar
och raderade pass slår igenom direkt. Radering av alla pass behåller utrustningen
och dess startvärden. Tidigare användning ska bara avse sådant som inte redan
finns i kopplade Kuta-pass, annars räknas den dubbelt.

Alla anrop kräver inloggning; skrivningar kräver `X-CSRF-TOKEN`:

| Anrop | Resultat |
|---|---|
| `GET /api/equipment` | Egen utrustning med startvärden och total användning. |
| `GET /api/equipment/last-used-shoe` | Senast använda aktiva sko, sorterad på passets starttid; `{ "id": null }` om ingen finns. |
| `PUT /api/equipment/{id}/retire` | Pensionerar egen utrustning och behåller gamla kopplingar. |
| `DELETE /api/equipment/{id}` | Raderar egen utrustning och dess kopplingar, men behåller passen. |
| `GET /api/equipment/kinds` | Tillåtna typer. |
| `POST /api/equipment` | Skapar utrustning med `name`, `kind`, valfritt `brand`, `initialDistanceM` och `initialDurationS`. Båda startvärdena är 0 om de utelämnas. |
| `PUT /api/activities/{id}/equipment` | Ersätter kopplingarna med `{"equipmentIds":["uuid"]}`; tom lista kopplar bort alla. Annans eller okänt pass ger 404. |

Vid skapande av pass skickas `equipmentIds` som JSON-array (manuellt) eller
upprepade formulärfält (FIT). Okänd eller annans utrustning avvisas utan att
passet eller kopplingarna ändras. API:t använder SI-enheter; webben visar kilometer.

## Taggar på aktiviteter

Ett pass kan ha flera taggar: `race` (Lopp), `workout` (Intervaller/kvalitet),
`long_run` (Långpass) och `easy` (Lugnt). Välj dem vid manuell registrering eller
FIT-uppladdning, ändra dem på passsidan och filtrera passlistan efter en tagg.

`activities.tags` är `text[]`, med tom array som standard och GIN-index.
API:t validerar mot `ActivityInput.Tags` och tar bort dubbletter; databasen har
avsiktligt ingen lista över tillåtna nycklar. Webben hämtar alternativen från
`GET /api/activity-tags`. En ny tagg kräver en nyckel i kodlistan och en översättning
i vardera `tagNames` i svenska och engelska språkfilen, ingen ny migrering.

Alla anrop kräver inloggning och gäller bara egna pass. Skapande tar `tags` som
JSON-array för manuella pass eller upprepade formulärfält för FIT. Ändra med
`PUT /api/activities/{id}/tags`, exempelvis `{"tags":["race","workout"]}`; tom array
tar bort alla taggar. Skrivningar kräver `X-CSRF-TOKEN`.
`GET /api/activities?tag=race` filtrerar före sidindelningen på 25 pass. Sidmarkörer och periodrubriker gäller det filtrerade urvalet.
`GET /api/records?tag=race&year=2026` ger bästa insatsen per distans i lopp under
UTC-året; utelämnat år ger personbästa i lopp. Filtren använder
`tags @> ARRAY['race']`, som stöds av GIN-indexet. Ändrade taggar slår igenom i
rekordurvalet direkt, utan omberäkning av insatserna.

## Bästa insatser och rekord

FIT- och Strava-importen räknar bästa insats för löpning över 1 km, 1 mile, 5 km, 10 km,
halvmaraton och maraton och sparar dem i samma transaktion som passet. Beräkningen
använder tids- och distansströmmen med linjär interpolering mellan mätpunkterna.
Tiden är förfluten tid inklusive pauser, inte rörelsetid. Ingen distans extrapoleras
före första eller efter sista mätpunkten. Saknade/ogiltiga värden, återställd distans
och icke stigande tid bryter strömmen; ett rekord får inte korsa en sådan brytpunkt.
Utan en sammanhängande ström över hela distansen blir det ingen insats.

`distanceM` är standarddistansens heltalsetikett: 1000, 1609, 5000, 10000, 21097
eller 42195. Motorn använder exakt 1609,344 m för milen och 21097,5 m för halvmaran.
`elapsedS` och `startOffsetS` är sekunder med decimaler. `achievedAt` är insatsens
starttid i UTC (passets starttid plus `startOffsetS`).

Alla anrop kräver inloggning och ger bara den egna användarens data:

| Anrop | Resultat |
|---|---|
| `GET /api/activities/{id}/best-efforts` | Passets bästa insatser, sorterade på distans; 404 för okänt eller annans pass. |
| `GET /api/records` | Snabbaste insatsen per distans; vid lika tid väljs den tidigaste. |
| `GET /api/records?year=2026` | Samma urval inom UTC-kalenderåret då insatsen började. |
| `POST /api/activities/{id}/best-efforts/recalculate` | Ersätter passets insatser från sparade tidsserier; 204 vid framgång, annars 404 för okänt eller annans pass. Kräver `X-CSRF-TOKEN`. |

Omberäkningen kan upprepas efter ändrad beräkningslogik och fungerar även utan
sparad råfil eller pulssamtycke. Kör den för befintliga pass som importerats innan
rekordmotorn infördes; de fylls inte automatiskt i vid migreringen. Pass utan
distansström och andra sporter ger en tom lista. Person- och årsbästa beräknas från
`best_efforts`, så borttagning av ett pass gör att nästa bästa insats tar över direkt.

Beräkningstesterna jämför också med en uttömmande referensberäkning på oregelbundna
strömmar. Integrationstesterna använder syntetiska FIT-filer skapade med Garmins SDK
och kontrollerar import, omberäkning, årsskifte, kaskadradering, CSRF och ägarskap.

## Importera en Strava-export

Öppna **Profil → Inställningar** via kugghjulet. Under **Importera från Strava** väljer du Stravas kompletta ZIP-export (högst 500 MB) och trycker **Importera pass**. Ta ställning till pulssamtycket först. Låt sidan vara öppen medan importen körs; stora arkiv kan ta flera minuter. Exporten måste innehålla `activities.csv` i roten och filerna på sökvägarna i kolumnen `Filename`.

Importen behåller passnamn, sport, starttid (UTC), distans i meter, tid och tillgängliga sammanfattningar från CSV. FIT och GPX, inklusive `.fit.gz` och `.gpx.gz`, ger tidsserier och rutter. Pass utan aktivitetsfil får en sammanfattning utan rutt. GPX-avstånd beräknas från GPS-punkterna; CSV-distansen behålls i sammanfattningen, så de kan skilja sig. Foton, kontakter, privata anteckningar, skor och annan utrustning importeras inte. Detta använder användarens arkiv, utan anslutning till Stravas API.

Alla importerade pass är privata. Puls och de enskilda råfilerna sparas bara med samtycke; ZIP-arkivet sparas inte som råfil. Återkallat samtycke raderar puls och råfiler även från importerade pass. Importen fyller inte på puls i redan importerade pass: ta bort och importera dem igen om de först laddades upp utan samtycke.

Efteråt visas antal importerade pass och dubbletter samt Strava-ID för pass som misslyckats (till exempel en saknad, trasig eller ej stödd aktivitetsfil). Tidigare sparade pass behålls om ett senare pass misslyckas eller anslutningen bryts. Kör samma export igen för att fortsätta; Strava-ID och identiska aktivitetsfiler förhindrar dubbletter inom kontot. Passlistan visar 25 pass åt gången; resten nås med Visa fler.

API: `POST /api/activities/import/strava`, multipart-fält `file`, med inloggning och CSRF-token. Svar: `{ imported, duplicates, failedActivityIds }`. Högst 20 000 pass, 100 000 ZIP-poster, 20 MB per uppackad fil och totalt 1 GiB uppackade aktivitetsfiler per anrop. Enskilda fel hindrar inte andra pass; ett ogiltigt CSV-manifest avvisar hela arkivet före lagring.

## Driftsättning (Fly.io)

En egen Fly-app (`kuta`, region `arn`). `Dockerfile` bygger webben och API:t till en image där API:t servar webben, så att de delar origin. Migreringarna körs när appen startar.

**Domän före deploy:** `CanonicalHost` i `fly.toml` är `kutaapp.com`. Konfigurera DNS för domänen mot Fly-appen och verifiera att Fly har ett giltigt TLS-certifikat för `kutaapp.com` innan du deployar denna konfiguration. Appen skickar vidare `kuta.fly.dev` och `www.kutaapp.com` till `https://kutaapp.com`.

**Supabase-projektet** (en gång, i dashboarden):
- EU-region. Stäng av Data API. Minsta lösenordslängd 8.
- Authentication → URL Configuration: Site URL `https://kutaapp.com` (bekräftelselänken byggs på den). Adressen ska följa `CanonicalHost` i `fly.toml` och vara inställd före deploy.
- Authentication → Email Templates → Confirm signup: innehållet i `supabase/templates/confirmation.html`.
- Authentication → Email Templates → Reset password: innehållet i `supabase/templates/recovery.html`. Krävs före användning av lösenordsåterställning i drift; standardmallen skickar sessionstokens till webbläsaren i stället för vårt API.
- Egen SMTP (Authentication → Emails → SMTP Settings): Scaleway TEM, host `smtp.tem.scaleway.com`, port 587, användare = Scaleway-projektets id, lösenord = secret key för en API-nyckel med bara TEM-behörighet (sparad i 1Password), avsändare på kutaapp.com. Domänen verifieras i Scaleway med SPF, DKIM och DMARC hos Hostup. Höj mejlgränsen under Authentication → Rate Limits. Supabases inbyggda mejl skickar bara till projektets egna medlemmar.

**Hemligheter** ligger i 1Password, aldrig i filer eller kommandon. [fly.secrets.tpl](fly.secrets.tpl) listar dem som `op://`-referenser; värdena finns i objektet `fly-kuta` i valvet `Development`, ett fält per hemlighet med hemlighetens namn:

| Fält | Värde |
|---|---|
| `ConnectionStrings__Default` | Supabase → Connect → Session pooler, i Npgsql-format: `Host=…;Port=5432;Database=postgres;Username=postgres.<ref>;Password=…;SSL Mode=Require;Maximum Pool Size=10` |
| `Supabase__SecretKey` | Supabase → Project Settings → API Keys → secret key (`sb_secret_…`) |
| `DataProtection__Certificate` | Certifikatet som krypterar Data Protection-nycklarna, base64 på en rad (se nedan) |
| `Storage__AccessKeyId`, `Storage__SecretAccessKey` | S3-nyckel för bucketen med råa FIT-filer (se nedan) |

**Lagring för råa FIT-filer:** en privat bucket som nås via S3-protokollet, i R2 (Cloudflare) eller Supabase Storage (Storage → S3 Connection). Lägg `Storage__ServiceUrl`, `Storage__Region` och `Storage__Bucket` i `fly.toml` under `[env]` och nyckeln i 1Password enligt tabellen. Utan dessa går appen att starta, men uppladdning och återkallat pulssamtycke ger fel.

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
