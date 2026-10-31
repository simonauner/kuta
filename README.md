# Kuta

Löparapp med Garmin-import, statistik, rekord och socialt flöde. Se `AGENTS.md` för beslut och nästa steg.

## Publik startsida och app

`/` är en statisk, svenskspråkig startsida (`web/index.html`, stilar i `web/src/landing.css`). Fotot i `web/public/images/forest-trail.webp` tillhör Simon Auner och serveras lokalt som WebP, utan beroende av Stravas bildserver; produktbilderna är riktiga appskärmdumpar med syntetiska exempeldata. Startsidan visar ett pass i mobilram, FIT-uppladdning samt träningsmål och löprekord i mobilen. `/features` är en statisk svensk funktionssida (`web/features.html`, `web/src/features.css`), länkad från startsidans funktionssektion. Bilderna i `web/public/images/features/` är lokala skärmdumpar med syntetiska data i mörkt tema. Statistiksektionen visar löprekord, utveckling, prognos, årsöversikt och passdetaljer med utfällda splits ovanför kartan i ett bildspel (`web/src/features.ts`); mobilbilderna är tagna vid mobilbredd i 3× upplösning (1170 × 2280 px) och visas i en CSS-telefonram med appens riktiga bottennavigering. Bildspelet kan pausas och styras manuellt, växlar bara när det syns och startar utan automatisk växling vid minskad rörelse. Utan JavaScript visas löprekorden. Träningsmål har en egen sektion med två mobilbilder i samma telefonram: skapa mål-modalen och Pass-sidan med ett pågående mål, ett uppnått mål och två veckors streak. Båda visar syntetiska data. Startsidan och funktionssidan delar bildstilar och telefonramar i `web/src/product-shots.css`, som importeras av `landing.css`. FIT-uppladdningen (1347 × 1080 px) och multisportvyn (2016 × 1584 px) är desktopbilder i 3× upplösning; utrustning visas i samma mobilformat som de andra telefonbilderna. De publika sidorna och appen delar systemtypsnitt och färgvariabler i `web/src/theme.css`; publika knappar följer appens storlek och hörnradie, och sidfotslänkarna staplas till höger. `Footer` i `web/src/components/footer.tsx` och dess CSS delas av alla sidor: Vite renderar varianten `public` till statisk HTML, medan appens `Layout` använder varianten `app` med valt språk och version. Sidorna följer systemets ljusa eller mörka tema med neutrala ytor och orange accent. Innehåll och SEO-metadata finns i HTML utan JavaScript. `robots.txt` och `sitemap.xml` finns i `web/public/`; canonical och sitemap använder `https://kutaapp.com/`.

Logotypen ”Spåret” finns som fristående SVG i `web/public/brand/` (mörk och ljus variant). Bokstäverna är vektorkonturer och kräver ingen font. Symbolen i `web/public/favicon.svg` används också av PWA-manifestet. PNG-ikoner för PWA finns i `web/public/icons/` (192/512 samt maskable 512 med extra marginal). `web/public/apple-touch-icon.png` (180×180, heltäckande bakgrund) används av iOS, och `favicon-32.png` är PNG-fallback för webbläsare. Båda HTML-dokumenten länkar till ikonerna; PWA:n startar på `/app`. SVG-underlagen för hemskärmsikonerna finns i `web/public/brand/app-icon.svg` och `app-icon-maskable.svg`.

`/privacy` är en publik, statisk integritetspolicy (`web/privacy.html`, extra stilar i `web/src/privacy.css`) med svenska och engelska i samma dokument. Engelska kan länkas direkt med `/privacy#english`. Båda språken är läsbara utan JavaScript; React-appens länkar använder dess valda språk. Startsidan och inloggningssidan länkar till policyn.

Policyn är ett utkast inför Garminansökan. Verifierat underlag, beslut, förslag och kvarvarande kontroller finns under [Integritet: underlag och öppna frågor](#integritet-underlag-och-öppna-frågor). Uppdatera båda språkversionerna när uppgifterna ändras; Garminanslutningen beskrivs som planerad tills den är implementerad.

React-appen öppnas på `/app`, med vyerna `/app/activities`, `/app/stats` samt `/app/profile` och `/app/settings`. `web/app.html` har `noindex`. Mejlets bekräftelselänk återvänder till `/app`. Vite bygger de tre HTML-filerna, och .NET serverar dem från samma origin. PWA:n startar på `/app` och använder bara appskalet som navigeringsfallback inom `/app`.

Profil visar löparens namn och egna pass med samma passkort och kartminiatyrer som andra löparprofiler, inklusive privata pass. Kugghjulet öppnar Inställningar på `/app/settings`, med länkar till Följare på `/app/settings/followers` och Utrustning på `/app/settings/equipment`, språkval, dolda platser, pulssamtycke, Strava-import, dataexport, e-postadress, utloggning och Danger zone för att radera alla pass eller kontot. Undersidorna visar hela hierarkin i brödsmulorna: Profil → Inställningar → Följare respektive Profil → Inställningar → Utrustning. Vanlig FIT-uppladdning och manuell registrering nås via Pass → Skapa ny aktivitet. Pulssamtycket kan ändras med samma checkbox både i filmodalen och under Inställningar. Checkboxen visas även när samtycket redan är givet.

## Språk och URL:er

React-appen använder `react-i18next` med svenska och engelska. Språkväljaren finns vid inloggning och under Inställningar i appen. Valet sparas lokalt i webbläsaren (`kuta.language`); utan sparat val prioriteras svenskt IP, sedan det första stödda språket i webbläsarens språklista, annars engelska. Bara uttryckliga val sparas. API:t bedömer svenska IPv4-/IPv6-nät med en lokal lista, utan externa IP-uppslag; källan och hur listan uppdateras finns i [Localization/README.md](api/Kuta.Api/Localization/README.md). Om uppslaget misslyckas eller tar mer än 1,5 sekunder används webbläsarens språk. Valet uppdaterar även HTML-dokumentets `lang`. Det synkas inte mellan enheter.

Appens URL:er är `/app` (flöde), `/app/activities`, `/app/stats`, `/app/profile` och `/app/settings` på båda språken.

Översättningar finns i `web/src/locales/sv.json` och `en.json`; lägg till samma nycklar i båda. API-felkoder översätts i webben, med ett generellt fel för okända koder. Mejlmallar och PWA-manifest är tills vidare svenska. Språkval och katalogernas täckning kontrolleras med `cd web && npm test` (Node enligt `.nvmrc`).

## Följare och flöde

Flödet på `/app` visar egna pass och pass från löpare du följer efter deras godkännande,
nyaste först, 25 åt gången. **Inställningar → Följare** innehåller inkommande förfrågningar
och listor över följare/följda. Det finns ingen användarkatalog eller namnsökning, varken i
webben eller API:t. **Dela profil** på den egna profilen kopierar en direktlänk till
`/app/runners/{id}` på aktuell origin, med bekräftelse och ett manuellt kopieringsfält om
webbläsaren nekar clipboard-åtkomst. Mottagaren loggar in för att se profilen och kan skicka
en följförfrågan därifrån. Länken ändrar inte passens synlighet eller följgodkännandet.
Godkänn eller avböj förfrågningar, avbryt egna förfrågningar, sluta följa eller ta bort följare.
Relationerna är riktade: ett godkännande innebär inte att båda automatiskt följer varandra.
Namn i flödet och följarlistorna länkar till `/app/runners/{id}`. Profilsidan visar löparens
namn, följstatus och de pass besökaren har läsåtkomst till, 25 åt gången. Där kan du också
sluta följa, avbryta en förfrågan eller skicka en ny. Efter avföljning försvinner följarpassen;
offentliga pass finns kvar på profilen. Egna privata pass visas bara på den egna profilen.

Nya pass är privata. På **Redigera aktivitet → Synlighet** kan ägaren välja `private`
(bara ägaren), `followers` (godkända följare) eller `public` (alla inloggade på Kuta med
länken). Flödet är inget globalt upptäcktsflöde: offentliga pass från andra än dem du följer
visas inte där. Privata pass blir aldrig synliga genom att en följförfrågan godkänns.
Nekad läsning eller ändring ger 404. Bara ägaren kan redigera, dela upp, räkna om rekord
eller radera pass. Befintliga listor, årsstatistik, utrustning och personliga rekord är egna.

Delade pass visar beskrivning, taggar, sportdelar, träningsdata och passets bästa insatser.
Puls (även tidsserier och varv), fullständiga GPS-tidsserier, klockmodell och utrustning
skickas bara till ägaren. Flödets och profilens passkort visar kartminiatyrer från den sparade
förenklade rutten. För andra än ägaren klipper PostGIS bort minst 500 meter runt originalets
start- och slutpunkt samt ägarens sparade dolda platser med respektive radie, även vid återbesök; förenkling sker före klippning och separata delar
förblir separata. Helt dolda eller saknade rutter ger ingen karta. Ägaren ser hela rutten.
Kartorna använder samma OpenFreeMap-leverantör som passsidan och laddas bara nära skärmen;
rullning och beröring styr sidan, inte miniatyrkartan. Titel och beskrivning är
användartext; användaren behöver själv välja vad den delar där.

API (alla kräver inloggning; skrivningar kräver CSRF):

- `GET /api/runners/{id}` ger `{ id, displayName, status }`; `status` är `none`, `pending`
  eller `accepted` för den egna utgående relationen. Okänd löpare ger 404.
  `GET /api/runners` (även med `?q=`) ger 404; användare kan inte listas eller sökas.
- `GET /api/runners/{id}/activities` ger `{ items, nextCursor }` för den löparens läsbara
  pass med samma sidmarkör och skyddade kartminiatyrer som flödet. Inga privata pass från
  andra, puls eller GPS-tidsserier ingår.
- `GET /api/follows` ger `{ following, followers }`, inklusive väntande förfrågningar.
- `POST /api/following/{id}` skickar en förfrågan, idempotent även efter godkännande.
- `PUT /api/followers/{id}` godkänner en inkommande förfrågan.
- `DELETE /api/following/{id}` avbryter en förfrågan eller slutar följa.
- `DELETE /api/followers/{id}` avböjer eller tar bort en följare. Borttagning återkallar
  läsåtkomst till följarpass vid nästa anrop; redan hämtade kopior kan inte återkallas.
- `GET /api/feed` ger `{ items, nextCursor }`. Nästa sida använder `beforeStartedAt` och
  `beforeId`, samma stabila markör som passlistan. Inga tidsserier eller puls ingår.
  `routeGeoJson` innehåller en förenklad GeoJSON-sträng
  (LineString/MultiLineString) för kartminiatyren, eller null. Delade rutter är klippta enligt
  start-/slutzonerna ovan; egna rutter är fullständiga.
- `PUT /api/activities/{id}` tar även `privacy`; utelämnat fält behåller synligheten.
  Passdetaljer innehåller även `userId`, `displayName` och `privacy`.

Migrering 014 skapar `follows` med unik riktad relation, förbud mot självföljning,
kaskadradering och index för inkommande förfrågningar. RLS är aktiverat och Data API-grants
förblir återkallade; BFF:ens SQL äger åtkomstkontrollerna. Läsregeln delas av flöde,
passdetaljer och passets rekord. API-svar har `Cache-Control: no-store` för att undvika
återanvändning efter ändrad åtkomst. Åtkomstmatris och flödets SQL-sidindelning testas direkt
i lokal Postgres med två användare. HTTP-tester täcker kopplingen för följförfrågan →
godkännande → delat pass → återkallelse, svarens dataskydd, ägarskap vid skrivning och
validering av sidmarkör. Dessa
kontroller behöver HTTP för att visa att rätt användare, statuskod och svar kopplas ihop.

## Passlistans sidindelning

`GET /api/activities?timeZone=Europe%2FStockholm` ger `{ items, nextCursor }`, högst 25 pass
sorterade på `startedAt` och `id` fallande. Nästa sida hämtas med `beforeStartedAt` och `beforeId`
från `nextCursor`; null betyder att historiken är slut. Utelämnad tidszon ger UTC, webben skickar
webbläsarens IANA-tidszon. Listans `showMonth` och `showWeek` anger om fler egna pass finns i
samma lokala år respektive månad, även utanför sidan. Därmed ändras inte rubriknivåerna när
”Visa äldre pass” fyller på listan. Inga tidsserier eller rutter ingår i listsvaren.

`GET /api/activities/years?timeZone=Europe%2FStockholm` ger alla år med egna pass, nyaste först.
Passlistans `year=2024` begränsar både första och efterföljande sidor till det lokala året.
Årsväljaren visas när historiken innehåller flera år; ”Alla år” är standard. Valet sparas i
appens URL (`/app/activities?year=2024`) och följer med till passsidan och tillbaka.

Passsidans kilometersplits visas både i en utfällbar panel ovanför kartan och under diagrammen.
Knapparna Varv och Splits ligger bredvid varandra; högst en av panelerna kan vara öppen åt gången.
De interpolerar tid och höjd vid varje kilometergräns mellan
mätpunkterna; snittpulsen vägs med tiden. Ingen sträcka eller tid läggs till utanför
inspelningens uppmätta ändpunkter. Återställd distans eller icke stigande tid gör att
splitstabellen utelämnas. En avslutande rest under 50 meter visas inte.
Diagram använder kilometer som x-axel när distansen ökar utan återställningar,
annars förfluten tid. Tidsaxelns etikett följer appens valda språk.

## Standardsynlighet för pass

Profil → Inställningar har ett automatiskt sparat standardval för nya pass: privat,
godkända följare eller alla inloggade löpare. Förvalet är privat. Manuella pass,
FIT-uppladdningar och Strava-importer läser det aktuella valet när varje pass sparas.
Enskilda pass kan fortfarande ändras via redigeringssidan.

**Tillämpa på alla pass** kräver bekräftelse och ersätter alla egna befintliga
synlighetsval med standardvalet. Andras pass påverkas inte. Standardvalet och
massändringen sparas atomiskt via `PUT /api/me/activity-privacy`
(`{ privacy: "private" | "followers" | "public", applyToAll?: boolean }`, CSRF krävs).
`GET /api/me` returnerar `defaultActivityPrivacy`, som även ingår i exportens
`profile.json`, och `allActivitiesMatchDefaultPrivacy`, som kontrolleras mot alla egna
pass. När alla redan har standardvalet är knappen inaktiv och visar en bock efter
knappen; det gäller även utan pass. Migrering 015 lägger till `users.default_activity_privacy`.
Samma profillås som importerna gör att en pågående import inte kan spara ett
äldre standardval efter massändringen. Importdubbletter behåller sina val.

## Dolda platser

Passens läsåtkomst följer synligheten och godkända följrelationer enligt ovan.
**Profil → Inställningar → Dolda platser** öppnar `/app/settings/privacy-places`, med
brödsmulan Profil → Inställningar → Dolda platser, där användaren kan spara flera namngivna
platser, till exempel Hem och Arbetsplats. Kartpunkten väljs genom klick, kartans
mittpunkt, webbläsarens platsåtkomst via **Använd min plats**, eller koordinatfält.
Platsåtkomst begärs bara vid knapptryck och hämtar en enda aktuell position, med
hög noggrannhet och 10 sekunders timeout. Punkten sparas först med Spara plats. Nekad/otillgänglig platsåtkomst och timeout visas
på valt språk utan att tidigare koordinater skrivs över. Varje plats har en radie på 50–5 000 meter, förval 200.
Platser kan redigeras och tas bort. Bara ägaren kan läsa och ändra dem via
`GET`/`POST /api/me/privacy-places` och `PUT`/`DELETE /api/me/privacy-places/{id}`.
Listan har `Cache-Control: no-store`. Migrering 014 skapar `privacy_places` med
ägarkoppling, kaskadradering, koordinat-/radiekrav och RLS utan Data API-grants.

Delade kartminiatyrer i flöde och profiler klipps i PostGIS mot ägarens aktuella platser,
med respektive radie, och minst 500 meter runt start och mål. Förenklingen sker före
klippningen; separata linjer sammanfogas aldrig över skyddade områden. Det gäller även
äldre pass. Ägaren får hela rutten, och delade detaljsvar utelämnar GPS-tidsserier helt.
Sparade privata platsers namn och mittpunkter skickas aldrig till andra mottagare.

Projektionen ändrar bara svarets koordinater. Sparad rutt, råfil, tidsserier, rekord,
splits och totalsiffror behålls. Dolda platser är inte en garanti för anonymitet.
Namn och mittpunkter lämnas aldrig till andra användare. Kartbilderna kommer från
OpenFreeMap, som ser vilket område användaren tittar på; namn och sparade platser
skickas inte dit som data. Ingen extern adressökning eller automatisk platsåtkomst används; platsåtkomst kräver
användarens knapptryck och webbläsarens behörighet.
Alla nya rutt-/export-/kartändpunkter måste använda samma skydd; originalrutt och råfil får inte
lämnas till andra mottagare. Ingen ny åtkomst följer av själva klippningen.

## Statistik över tid

`/app/stats` visar årsstatistik med månadsdiagram och tabell för distans, träningstid,
snittempo och snittpuls samt antal pass. Välj år och sportgrupp (se [Sportgrupper](#sportgrupper));
löpning och innevarande år är förval. Årsväljaren innehåller hela den egna historiken. Tomma månader har noll mängd,
men saknat tempo och puls visas som `–`.

`GET /api/activities/statistics?year=2026&sportGroup=running&timeZone=Europe%2FStockholm`
ger tolv månadsrader för den inloggade användaren. År krävs (1–9998), utelämnad sportgrupp
betyder alla sporter (okänd grupp ger `invalidSportGroup`) och utelämnad tidszon UTC. Summeringen sker i Postgres över hela
året, oberoende av passlistans sidindelning, utan att skicka tidsserier eller rutter till webben.
Tid inkluderar pauser. Tempo är summan av tid delad med summan av distans för pass med
positiv tid och distans (sekunder per kilometer). Snittpuls tidsvägs bara över pass med
positiv tid och sparad puls. Alla sporter kombinerar även tempo och puls mellan sporter; samlade multisportpass ingår inte i snittempot. Vid gruppfilter räknas hela pass i gruppen och motsvarande delar av multisportpass i andra grupper, med puls från delens mätpunkter. Totalvyn räknar varje pass en gång.
Månader och årsgränser följer webbläsarens tidszon, och hela passet tillhör startmånaden.
Statistikcachen uppdateras vid skapande, import och radering av pass och rensas när
pulssamtycke återkallas.

**År för år** på samma sida visar ackumulerad distans, tid eller antal pass per år på samma
kalender för vald sportgrupp. Diagrammet visar valt år framhävt och upp till fem senaste år.
Tabellen jämför alla år till och med dagens månad och dag (så att skottdagen inte förskjuter
jämförelsen), med förändring mot året före och helårssumma.
`GET /api/activities/statistics/days?sportGroup=running&timeZone=Europe%2FStockholm` ger
summor per lokal dag (`day`, `count`, `distanceM`, `durationS`) över hela den egna historiken,
bara dagar med pass. Summeringen följer målens regler (`SportGroups.EffectiveCte`): hela passet
hör till startdagen, en grupp räknar matchande delar av multisportpass och utan grupp räknas
varje pass en gång. Okänd grupp ger `invalidSportGroup` och ogiltig tidszon `invalidTimeZone`.

## Träningsbelastning

Statistiksidan visar kondition, trötthet och form för löpning, och Pass visar en varning högst upp när
konditionen ökar snabbt. Modellen är TrainingPeaks förenkling av Banisters impuls–respons-modell:

- **Belastning per pass (rTSS):** `∫ v² dt / vT² / 36`, där `vT` är tröskelfarten. En timme i tröskelfart ger 100.
  `activities.run_intensity` (migrering 018) lagrar `∫ v² dt` (m²/s) för passets löpning, eller bara löpdelarna i
  ett multisportpass. Den räknas av `BestEfforts.ReplaceAsync`, alltså vid FIT- och Strava-import, uppdelning och
  omberäkning av rekord. Farten jämnas ut över minst 30 s, ett uppehåll över 10 s (autopaus) bildar ett eget
  avsnitt, och trasiga värden bryter strömmen. Pass utan distansström (manuella och äldre pass) uppskattas ur
  snittfarten, `distans² / tid`. Backar justeras inte.
- **Tröskelfart:** `users.threshold_speed_m_s` om användaren angett den, annars farten som VDOT-prognosen räcker
  till i en timme (`RacePredictor.ThresholdSpeed`). Utan någon av dem finns ingen belastning. Samma tröskel gäller
  hela historiken.
- **Kondition (CTL) och trötthet (ATL):** exponentiella medelvärden av daglig belastning med 42 respektive 7 dagar
  (`x += (belastning − x) / N`), från första dagen med löpning till idag i webbläsarens tidszon. Form (TSB) är
  kondition minus trötthet samma dag.
- **Varning:** när konditionen har ökat mer än 7 på en vecka, och först efter mer än 42 dagars historik så att en ny
  användare inte varnas medan konditionen byggs upp från noll. Texten är information, inte ett medicinskt råd.

`GET /api/training-load?timeZone=Europe%2FStockholm` ger `thresholdSpeedMS`, `thresholdSource`
(`manual`/`prediction`/`null`), `predictedThresholdSpeedMS`, `days` (de senaste 182 dagarna med `load`, `fitness`,
`fatigue` och `form`), `rampPerWeek` och `warning`. `PUT /api/me/threshold-speed` med `{ "thresholdSpeedMS": 4 }`
sparar en egen tröskelfart (1:40–60:00 per km, annars `invalidThresholdSpeed`), och `null` går tillbaka till
prognosen. Tröskelfarten ingår i dataexportens `profile.json`. Bara egna pass räknas.

## Träningsmål

**Pass → Nytt mål** skapar ett mål: distans (km), tid (minuter) eller antal pass per vecka,
månad eller år, för en grupp av träningsformer eller alla. Målet upprepas varje period eller gäller en gång,
för perioden då det skapas (exempelvis 1 000 km i år). Pågående mål visas som kort med
förloppsstapel ovanför passlistan; ett uppnått mål får ”Hurra! Målet är nått”. Upprepade mål
visar även förra periodens resultat när det nåddes och hur många perioder i rad målet nåtts
(från två). Ett avslutat engångsmål visas nedtonat tills det tas bort. Klick på kortet öppnar
redigering och borttagning i samma modal. Inga mål ger ingen sektion.

Varje mål har en egen synlighet i måldialogen: bara jag (förval), godkända följare eller alla
inloggade med länken. Det finns ingen global inställning för mål. Egna målkort visar valet.
Profiler visar de mål besökaren får se med förlopp under rubriken Mål, utan redigering; inga
synliga mål ger ingen sektion. Läsregeln är passens: ägaren, `public` för alla inloggade och
`followers` bara vid godkänd följrelation.

Mål gäller [sportgrupper](#sportgrupper), inte exakta sporter, och dialogen visar vilka
träningsformer den valda gruppen räknar.

Summeringen följer statistikens regler: hela passet hör till perioden där det startar, tid
inkluderar pauser, redigerade multisportdelars distans används och ett gruppmål räknar
matchande delar av multisportpass. Utan grupp räknas varje pass en gång. Veckor börjar på
måndag och perioder följer webbläsarens tidszon. Ett mål räknas från början av perioden då det
skapas, även pass före skapandet (ett årsmål i oktober räknar från 1 januari), men aldrig
tidigare perioder. Ett nytt upprepat mål visar därför förra perioden och raden först från nästa
period. Raden räknar uppnådda perioder bakåt från förra perioden till första missade eller
startperioden, plus den pågående när den är uppnådd. Ett ändrat mål behåller sitt startdatum.

API (inloggning krävs; skrivningar kräver CSRF; bara egna mål, andras ger 404):

- `GET /api/goals?timeZone=Europe%2FStockholm` ger målen med `current` och `previous`
  (`{ start, end, value, achieved }`, `end` exklusivt; `previous` är null för engångsmål och under startperioden),
  `streak` och `ended`.
- `POST /api/goals` och `PUT /api/goals/{id}` tar `{ sportGroup, metric, period, target, recurring, timeZone, privacy }`.
  Utelämnad `privacy` ger privat vid skapande och behåller valet vid redigering. Svaren har `privacy`.
  `sportGroup` är en gruppnyckel eller null = alla, `metric` är `distance`, `duration` eller `count`, `period` är `week`,
  `month` eller `year`, `target` i meter, sekunder eller hela pass (högst 100 000 000 m eller s, respektive
  10 000 pass). `timeZone` avgör startdatumet vid skapande. Ogiltiga värden ger `invalidGoal`.
- `DELETE /api/goals/{id}`.
- `GET /api/runners/{id}/goals?timeZone=…` ger löparens mål som besökaren får se, filtrerade
  per mål (ägaren ser alla); okänd löpare ger 404.

Migrering 016 skapar `goals` med ägarkoppling, kaskadradering vid kontoradering och RLS utan
Data API-grants; migrering 017 lägger till `goals.privacy`, som även ingår i exportens `goals.json`. Radering av alla pass behåller målen. Målen ingår i exportens `goals.json`.
Periodlogik, grupper och validering testas som enhetstester, summering per tidszon, sportgrupp, multisportdelar
och ägarskap i lokal Postgres, och HTTP-testet täcker routing, statuskoder, svarskontrakt och
att en annan användare varken ser eller ändrar målet. Synligheten per mål (ägare, godkänd och
väntande följare, okänd, oförändrat val vid redigering) testas i Postgres; ett HTTP-test visar
att en annan inloggad bara får löparens publika mål.

## Sportgrupper

Mål och statistik räknar grupper av träningsformer, inte exakta sporter: Löpning räknar löpning,
terränglöpning och virtuell löpning; Cykling alla cykeltyper inklusive virtuell; Gång och
vandring även snöskovandring; Längdskidåkning även topptur och rullskidor; Paddling kanot, kajak
och SUP; Rodd även virtuell rodd. Simning i bassäng och öppet vatten är samma sport (`swimming`),
eftersom Garmin anger vattentypen som undertyp och Strava inte skiljer dem åt. Övriga sporter är
egna grupper med sportens nyckel. Grupperna definieras bara i `SportGroups.cs`, även med Garmins
råa sportnamn (exempelvis `crosscountryskiing`); `GET /api/activities/sport-groups` ger
`[{ key, sports }]` och webben namnger grupperna under `sportGroups` i språkfilerna. Passlistans
sportfilter och passens egna sport är oförändrade.

## Applikationsloggar och integritet

API:t ersätter .NET:s loggproviders med `PrivateLogProvider`. Den skriver JSON med fasta händelser och operationer för start, stopp, requestfel, startfel och migreringsfel. Requestfel har även HTTP-status och tidsåtgång i millisekunder. Loggformatterare, godtyckligt loggstate, scopes och kategorinamn används aldrig, även om loggnivån höjs. DbUp:s egen konsolloggning är avstängd. Samma skydd används lokalt och i drift.

Undantag projiceras till en begränsad modell: typnamn, metodernas typ- och metodnamn samt relativa källkodssökvägar och radnummer från debugsymboler. Bara källfiler under projektets build-rot får en sökväg; saknade symboler eller externa filer ger ingen fil- eller radinformation. Dynamiska metoder och typnamn från dynamiska assemblies utelämnas eller ersätts med en fast etikett. Generiska typer visas utan konkreta typargument. Felkedjor, inklusive alla grenar i `AggregateException`, begränsas till totalt åtta undantag och högst 32 stack frames per undantag. Undantagsmeddelanden, formaterade stackspår, `Data`, `Source`, `HelpLink` och andra godtyckliga egenskaper serialiseras aldrig. Om metadata inte kan läsas används en fast reservetikett.

Inga request bodies, URL:er, querysträngar, headers, cookies, tokens, mejladresser, lösenord, puls, GPS-rutter eller uppladdade filers namn/innehåll skrivs av applikationsloggningen. Automatiska HTTP-, databas- och ramverksloggar släpps inte igenom. Felsökning använder feltyp, kodposition och driftmått; råa felmeddelanden får undersökas med syntetiska lokala reproduktioner.

Nya logghändelser ska använda granskade strukturerade fält utan användarvärden och testas med känsliga syntetiska värden. Okända fält utelämnas som standard. Använd inte `Console.WriteLine`/stderr för dynamiska värden, serialisera inte råa undantag och lägg inte till andra loggproviders vid sidan av integritetsgränsen. Regex-maskning av fritext är inte en garanti mot personuppgifter.

Framtida loggmottagare får samla in den begränsade konsolutmatningen. En direkt SDK-integration som Sentry måste granskas separat: automatisk insamling av exceptions, requests, breadcrumbs, användarkontext och tracing passerar inte nödvändigtvis loggprovidern. Aktivera den inte utan en testad tillåtelselista för vad som får skickas. Detta skydd omfattar Kutas applikationsloggar, inte Flys egna proxy-/plattformloggar, Supabases loggar eller historiska loggar.

## Multisport och uppdelning av importerade pass

En multisport-FIT importeras som ett pass med ordnade sportdelar, inklusive registrerade växlingar. Simning → cykling → löpning identifieras som triathlon, kombinerad simning/löpning som swimrun och övriga kombinationer som multisport. Sessionernas start, tid och distans bevaras; kadens tolkas för respektive sport. Vid multisport-FIT i ett Strava-arkiv används FIT-filens sportdelar, starttid och totalsammanfattning; CSV-titeln behålls. Stravas separata aktiviteter slås inte ihop automatiskt.

På skapa- eller redigeringssidan väljer du **Dela upp i sporter**, väljer passtyp och justerar sportbyten med reglage eller sekunder från passets start. **Dela här** delar den valda delen i mitten; för swimrun får den nya delen motsatt sport. **Slå ihop med föregående** tar bort en gräns och behåller föregående sport. Klicka på en del för att granska dess karta och diagram innan du sparar. Ändringarna är ett lokalt utkast tills sidans gemensamma **Spara** används; **Avbryt** lämnar det sparade passet oförändrat. Passsidan visar bara resultatet och val av sportdel, med länken **Redigera aktivitet** till uppdelningsverktyget. Hela passets karta använder olika färger för sporterna. Simtempo visas per 100 meter, cykling i km/h och löpning per kilometer.

Uppdelningen kräver minst två sporter och en sammanhängande, stigande tidsserie med fullständig, icke minskande distans. Delarna täcker hela inspelningen utan luckor eller överlapp; gränser mellan mätpunkter interpoleras. Högst 200 delar stöds. Sammanfattningar utan tidsserier kan inte klippas. Funktionen ändrar sportindelningen och reparerar inte felaktig GPS eller simdistans.

FIT-uppladdning tar ett valfritt multipartfält `parts` med `{ kind, parts: [{ sport, startS, endS, useEquipment }] }`. `kind` är `triathlon`, `swimrun` eller `multisport`; sport kan även vara `transition`. Triathlon kräver simning, cykling och löpning i den ordningen (med valfria växlingar). Swimrun tillåter simning och löpning (med valfria växlingar). Passets valda utrustning räknas bara på delar där `useEquipment` är sant; olika utrustningsval per del stöds ännu inte. FIT-importen markerar löpning/gång/vandring som standard och även simning för swimrun. Växlingstid finns kvar i den totala förflutna tiden.

Redigeringssidan sparar uppdelningen som `multisport` tillsammans med titel, beskrivning, taggar och utrustning i `PUT /api/activities/{id}`. **Återställ importerad version** kräver bekräftelse, förhandsvisar originaldelarna och skickar `restoreParts: true` först när formuläret sparas. Allt sparas atomärt. De separata API-anropen `PUT`/`DELETE /api/activities/{id}/parts` finns också för uppdelning/återställning. Originalets sammanfattning, tidsserier, filhash och råfil behålls vid redigering. Inga extra pass skapas. Utrustningstotaler och distans uppdateras från den gällande uppdelningen; löprekord räknas om i samma transaktion och kan aldrig sträcka sig över flera löpdelar. Ändringsanropen kräver inloggning, ägarskap och CSRF-token. Sportdelar innehåller ingen kopia av pulsdata, så återkallat pulssamtycke fortsätter att radera all sparad puls på samma ställen.

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

### Kontoradering

Inställningar → Danger zone → **Radera konto** kräver att användaren skriver ett slumpat löparord. `DELETE /api/me` kräver inloggning och CSRF-token och använder endast den inloggade användarens id. Under samma profillås som importen raderas råfilerna via objektlagringen, därefter raderas `auth.users` direkt i databastransaktionen. Befintliga FK-kaskader tar bort profil, utrustning, pass, tidsserier, varv, sportdelar, rekord och Supabase-/Kuta-sessioner. Cookien och webbens sessionscache rensas; gamla cookies avvisas vid nästa anrop.

Om filrensningen misslyckas behålls kontot och filreferenserna för ett nytt försök. Redan raderade filer kan inte återställas om ett senare steg misslyckas, men raderingen kan köras igen. Leverantörsloggar och säkerhetskopior omfattas inte av appens radering; se integritetsunderlaget nedan. Ingen ny migrering eller separat Auth-adminnyckel behövs.

### Lösenordsåterställning

Välj **Glömt lösenord?** på inloggningssidan. Mejlet finns i instansens Mailpit och länkar till `/api/auth/reset-password`. API:t verifierar länken och ger en separat krypterad HttpOnly-cookie som gäller i högst 15 minuter och bara för lösenordsbytet. Efter bytet avslutas kontots Kuta-sessioner och användaren loggar in med det nya lösenordet. Om anslutningen bryts under lösenordsbytet avslutas sessionerna också, eftersom bytet kan ha hunnit lyckas hos Supabase. Begär då en ny återställningslänk vid behov. Både begäran och lösenordsbytet kräver CSRF-token; ogiltiga, använda och utgångna länkar kräver en ny begäran.

Den lokala mejlmallen anges i `supabase/config.toml`. Om en redan körande miljö saknar återställningsmallen måste dess ägare stoppa och starta just den Supabase-instansen för att läsa in konfigurationen. Ändra inte en lånad instans.

### Mejlmallar

`supabase/templates/confirmation.html` och `recovery.html` är fristående svenska HTML-mallar för de två mejlflöden som appen använder. De följer appens neutrala ytor, rundade hörn och orange primärfärg, med inline-stilar som grund och mörkt tema i mejlklienter som stöder `prefers-color-scheme`. De använder systemtypsnitt och inga bilder eller externa typsnitt. Layouten är tabellbaserad och anpassas till skärmbredden; varje knapp har en kopierbar reservlänk.

Behåll `{{ .SiteURL }}` och `{{ .TokenHash }}` i både knappen och reservlänken: bekräftelse går till `/api/auth/confirm`, återställning till `/api/auth/reset-password`. Ersätt inte dessa med `{{ .ConfirmationURL }}`, eftersom Kutas API ska ta hand om sessionen. Ämnesraderna finns i `supabase/config.toml`; kopiera även dem när mallarna läggs in i det hostade projektets **Authentication → Email Templates**. En ändring i repot uppdaterar inte hostade mallar automatiskt. Kontrollera båda mejlen i lokal Mailpit enligt [lokal körning](#köra-lokalt), och i faktiska mejlklienter före driftsättning eftersom deras HTML- och mörkerlägesstöd varierar.

Tester (kräver `supabase start`, med Storage påslaget): `dotnet test --project api/Kuta.Api.Tests`, eller `KUTA_PORTS=BAS dotnet test …` mot en egen instans.

API-jobbet i GitHub Actions använder `KUTA_PORTS=18000` och flyttar Supabase-konfigurationens portar i jobbets checkout. Portarna 18020–18029 ligger utanför Linux normala intervall för tillfälliga utgående anslutningar, så nedladdning av Docker-images inte kan ta databasporten före start. Lokal standardkonfiguration ligger kvar på 553xx.

Frontendkontroller med Node-versionen i `.nvmrc`: kör `npm ci` i `web/`, sedan
`npm run format:check`, `npm run lint`, `npm test` och `npm run build`.
`npm run format` formaterar webbfilerna med Prettier. API-klientens tester använder
Nodes inbyggda testverktyg och mockade nätverksanrop; de kräver inte Supabase.
GitHub Actions kör frontendkontrollerna i `.github/workflows/frontend.yml` vid
ändringar i `web/`, `.nvmrc` eller workflowfilen. API-jobbet i
`.github/workflows/api.yml` bygger och kör hela `api/Kuta.Api.Tests` i Release vid
ändringar i `api/`, `supabase/`, `global.json` eller workflowfilen. Båda körs på
pull requests med dessa sökvägsfilter; `Dockerfile`, `.dockerignore` och
`.github/workflows/deploy.yml` startar båda.
Ändringar i enbart `.md`-filer startar inga PR-kontroller. Vid varje push till
`main` anropar `.github/workflows/deploy.yml` båda jobben utan sökvägsfilter och
deployar bara om båda lyckas; se [Driftsättning](#driftsättning-flyio).

API-jobbet installerar .NET enligt `global.json` och Supabase CLI 2.72.7, startar
en tillfällig lokal Supabase med Auth, Mailpit och Storage, läser dess lokala
nycklar och väntar på auth efter omstart innan testerna körs. Testerna körs
sekventiellt (`-- --parallel none`). Inga produktionshemligheter eller hostade
tjänster används. Supabase stoppas även om testerna misslyckas.

Footern visar copyright och webbens byggnummer (`ÅÅÅÅMMDD.ttmmss`, UTC), som sätts
automatiskt av Vite vid bygget, även i Docker. I utvecklingsläge visas `Version utveckling`.

## Råfiler vid misslyckad import

Om uppladdning eller databascommit misslyckas släpps transaktionen innan en ny
anslutning kontrollerar om råfilen används av ett sparat pass. Kontrollen håller
profilens lås, så en samtidig import inte kan återanvända filnyckeln medan den
städas. Ett förlorat commitsvar får därmed inte radera råfilen för ett sparat pass.
Om databasen inte kan bekräfta utfallet lämnas filen kvar och felet loggas av
API:t. En sådan fil kan behöva städas manuellt när databasen åter är tillgänglig;
kontrollera först att inget pass refererar till nyckeln.

## Utrustning och användning

Aktiv utrustning visas före pensionerad. Inom grupperna behålls ordningen efter när utrustningen lades till.

Lägg till utrustning i modalen under Inställningar → Utrustning och koppla den till manuella pass eller FIT-importer. Välj ”Redigera utrustning” i utrustningens meny för att ändra namn, märke och tidigare användning i samma modal. Passkopplingar och pensionsstatus behålls. Skor kan även skapas direkt med ”Skapa ny” vid skofältet; den nya skon väljs automatiskt utan att aktivitetsutkastet försvinner.
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
| `PUT /api/equipment/{id}` | Uppdaterar egen utrustning med samma fält och validering som vid skapande. Annans eller okänd utrustning ger 404. |
| `PUT /api/equipment/{id}/retire` | Pensionerar egen utrustning och behåller gamla kopplingar. |
| `DELETE /api/equipment/{id}/retire` | Återaktiverar egen utrustning så att den kan väljas igen; historik och totaler behålls. |
| `DELETE /api/equipment/{id}` | Raderar egen utrustning och dess kopplingar, men behåller passen. |
| `GET /api/equipment/kinds` | Tillåtna typer. |
| `POST /api/equipment` | Skapar utrustning med `name`, `kind`, valfritt `brand`, `initialDistanceM` och `initialDurationS`. Båda startvärdena är 0 om de utelämnas. |
| `PUT /api/activities/{id}` | Sparar titel, beskrivning, taggar och utrustning tillsammans med `{"title":"Morgonpass","description":null,"tags":["race"],"equipmentIds":["uuid"]}`; tomma listor tar bort respektive val. Annans eller okänt pass ger 404. |

Vid skapande av pass skickas `equipmentIds` som JSON-array (manuellt) eller
upprepade formulärfält (FIT). Okänd eller annans utrustning avvisas utan att
passet eller kopplingarna ändras. API:t använder SI-enheter; webben visar kilometer.

Passsidan visar bara kopplad utrustning och valda taggar. Knappen Redigera aktivitet
öppnar `/app/activities/$activityId/edit`, där titel, beskrivning, taggar och utrustning ändras med gemensamma Spara och
Avbryt. Spara uppdaterar allt i samma transaktion; Avbryt lämnar passet oförändrat.

## Taggar på aktiviteter

Ett pass kan ha flera taggar: `race` (Lopp), `workout` (Intervaller/kvalitet),
`long_run` (Långpass) och `easy` (Lugnt). Välj dem vid manuell registrering eller
FIT-uppladdning, ändra dem via passsidans Redigera aktivitet och filtrera passlistan efter en tagg.
Listans taggfilter sparas i URL:en (`/app/activities?tag=race`), kan kombineras
med `year` och följer med till pass, skapandeformulär och tillbaka till listan.

`activities.tags` är `text[]`, med tom array som standard och GIN-index.
API:t validerar mot `ActivityInput.Tags` och tar bort dubbletter; databasen har
avsiktligt ingen lista över tillåtna nycklar. Webben hämtar alternativen från
`GET /api/activity-tags`. En ny tagg kräver en nyckel i kodlistan och en översättning
i vardera `tagNames` i svenska och engelska språkfilen, ingen ny migrering.

Alla anrop kräver inloggning och gäller bara egna pass. Skapande tar `tags` som
JSON-array för manuella pass eller upprepade formulärfält för FIT. Ändra med
`PUT /api/activities/{id}` med `title`, `description`, `tags` och `equipmentIds`, exempelvis
`{"title":"Morgonpass","description":null,"tags":["race","workout"],"equipmentIds":[]}`; tom array
tar bort alla taggar. Skrivningar kräver `X-CSRF-TOKEN`.

### Träningsformer

Träningsformen visas med en ikon efter titeln i passlistan och på passidan, med översatt namn som skärmläsaretikett och vid hovring. Efter år och taggar finns ett filter för träningsform. År, tagg och träningsform bevaras i URL:en mellan passlista, passida och formulär. `GET /api/activities?sport=cycling&tag=race&year=2013` kombinerar filtren före sidindelning och beräkning av periodrubriker. Sportvärdena finns i `ActivitySports` (API) och `web/src/lib/sports.ts` (webb); sportval och översättningar täcker Stravas träningsformer samt Annat.

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

Alla anrop kräver inloggning. Rekord och historik gäller den egna användaren;
passets bästa insatser följer passets läsregel:

| Anrop | Resultat |
|---|---|
| `GET /api/activities/{id}/best-efforts` | Passets bästa insatser, sorterade på distans; 404 för okänt eller oläsbart pass. `recordStatus` är `current` (nuvarande personbästa), `previous` (tidigare personbästa) eller `null`. För andra än ägaren är status alltid `null`, så privata pass inte kan påverka synlig rekordstatus. |
| `GET /api/records` | Snabbaste insatsen per distans; vid lika tid väljs den tidigaste. |
| `GET /api/records?year=2026` | Samma urval inom UTC-kalenderåret då insatsen började. |
| `GET /api/records/trend?distanceM=5000&timeZone=Europe/Stockholm` | `months`: bästa insatsen per lokal kalendermånad (bara månader med insatser). `progression`: rekordhistoriken, varje insats som var strikt snabbare än alla tidigare; lika tid behåller det äldre rekordet. 400 för annan distans än etiketterna ovan eller ogiltig tidszon. |
| `GET /api/records/predictions` | Loppprognos: `basis` (underlagets insats), `vdot` och `predictions` med `distanceM`/`elapsedS` för alla rekorddistanser. Tomt underlag ger `null` och en tom lista. |
| `POST /api/activities/{id}/best-efforts/recalculate` | Ersätter passets insatser från sparade tidsserier; 204 vid framgång, annars 404 för okänt eller annans pass. Kräver `X-CSRF-TOKEN`. |

På passets redigeringssida kan ägaren välja **Undanta från löprekord** vid exempelvis GPS-glapp. `PUT /api/activities/{id}` sparar `excludeFromRecords` (boolesk, förval false; utelämnat fält behåller valet). Passdetaljer och export innehåller markeringen. Hela passet filtreras bort från personbästa, årsrekord, taggfiltrerade rekord, rekordutveckling och prognoser. Träningsstatistik och mål påverkas inte. Passets beräknade insatser behålls och visas fortfarande på passsidan; undantaget överlever omberäkning och redigering av sportdelar och kan ångras utan ny import.

Omberäkningen kan upprepas efter ändrad beräkningslogik och fungerar även utan
sparad råfil eller pulssamtycke. Kör den för befintliga pass som importerats innan
rekordmotorn infördes; de fylls inte automatiskt i vid migreringen. Pass utan
distansström och andra sporter ger en tom lista. Person- och årsbästa beräknas från
`best_efforts`, så borttagning av ett pass gör att nästa bästa insats tar över direkt.

Prognosen (`RacePredictor`) använder Jack Daniels och Jimmy Gilberts formler: syreupptag
vid farten och andel av maxkapaciteten under tiden ger VDOT, och samma VDOT ger
likvärdiga tider på andra distanser (bisektion över tiden). Underlaget är insatsen med
högst VDOT bland 5 km och längre de senaste 90 dagarna (lika VDOT: den senaste);
kortare insatser ur intervallpass överskattar uthålligheten. VDOT över 90, över
världsrekordnivå, behandlas som mätfel. Statistiksidan visar personbästa och årsbästa
per distans, ett diagram med månadsbästa och rekordtrappa, rekordhistoriken som tabell
och prognosen. Passidan visar en separat lista över personbästa som sattes på passet,
med en trofé i guld och medaljer i silver med metallisk glans för nuvarande respektive tidigare
rekord. Status visas i tooltips som öppnas med mus, tangentbord eller tryck. Samma märkning visas
vid bästa insatser; en ljusgrå rosett i kontur markerar årsbästa när insatsen inte är ett personbästa.
Personbästamärkningen visas också
vid passlänkarna i rekordhistoriken. Status följer insatsernas tidpunkt, oberoende av
importordning, och räknas från nu sparade insatser även efter radering eller omberäkning.

Beräkningstesterna jämför också med en uttömmande referensberäkning på oregelbundna
strömmar. Integrationstesterna använder syntetiska FIT-filer skapade med Garmins SDK
och kontrollerar import, omberäkning, årsskifte, kaskadradering, CSRF och ägarskap.

Aktiviteter kan ha en valfri beskrivning på högst 10 000 tecken. `description`
skickas i JSON för manuella pass och som formulärfält vid FIT-uppladdning.
Tom text sparas som null. Beskrivningen behålls när en FIT-fil väljs i formuläret
och visas som vanlig text med radbrytningar på passsidan. Migrering 012 lägger
till fältet; redan importerade dubbletter hoppas fortfarande över vid Strava-import.

## Exportera data

Öppna **Profil → Inställningar → Exportera data → Ladda ner ZIP**. Låt sidan vara öppen medan arkivet förbereds. `GET /api/me/export` kräver inloggning och hämtar bara det egna kontots data, oberoende av filter och sidindelning i passlistan. Svaret är en nedladdningsbar `kuta-export.zip` med `Cache-Control: no-store`.

- `profile.json`: id, e-postadress, visningsnamn, profilens skapandetid och tidpunkt för pulssamtycke.
- `equipment.json`: all egen utrustning, även pensionerad, med startvärden och total användning.
- `privacy-places.json`: egna dolda platser med namn, mittpunkt och radie. Listan ingår även utan pass.
- `goals.json`: egna träningsmål med sportgrupp, typ, period, mål i SI-enheter, upprepning, startdatum och synlighet.
- `activities/<id>.json`: källa, skapandetid, synlighet, Strava-ID, sökväg till råfilen (eller `null`), `activity` med samma passdetaljer som passsidan och `bestEfforts` med passets bästa insatser. Detaljerna innehåller beskrivning, taggar, utrustnings-ID:n, tidsserier, varv samt originaldelar och redigerade multisportdelar.
- `raw/<id>.fit` eller `.gpx`: sparade råfiler med oförändrat innehåll. Manuella pass saknar råfil. Råfiler som inte sparats utan pulssamtycke, eller som raderats vid återkallat samtycke, kan inte exporteras. Gzip-filer från Strava har packats upp vid importen.

JSON använder lowerCamelCase, SI-enheter och UTC-tid. Lösenord, sessionscookies och tokens ingår inte. Ett konto utan pass får fortfarande profil, utrustningslista, dolda platser och mål. Arkivet byggs färdigt på en tillfällig fil före HTTP-svaret; den raderas vid fel, avbrott eller när överföringen är klar. Profil och pass låses mot skrivning under bygget för att samtyckesåterkallelse eller passradering inte ska kunna ta bort en råfil mitt i exporten. Om en förväntad råfil saknas misslyckas exporten i stället för att tyst lämna bort den.

Exportens innehåll, åtkomst mellan två användare, råfilernas bytes och samordningen med återkallat samtycke testas på databas-/lagringsnivå. HTTP-testet verifierar den nya routens autentisering, ZIP-svar och cache-/nedladdningsheaders; det upprepar inte innehållstesterna.

## Importera en Strava-export

Öppna **Profil → Inställningar** via kugghjulet. Under **Importera från Strava** väljer du Stravas kompletta ZIP-export (högst 500 MB) och trycker **Importera pass**. Ta ställning till pulssamtycket först. Under importen visas antal bearbetade pass och löpande räknare för importerade, dubbletter och misslyckade. Låt sidan vara öppen medan importen körs; stora arkiv kan ta flera minuter. Exporten måste innehålla `activities.csv` i roten och filerna på sökvägarna i kolumnen `Filename`.

Träningsformen bevaras även för exempelvis terränglöpning, elcykling och virtuella pass. Namn från Stravas CSV och kompakta sportnamn normaliseras vid importen; okända framtida namn sparas också. Äldre importer slog ihop Run/Trail Run/Virtual Run respektive Ride/Virtual Ride/E-Bike Ride. Dessa redan sparade pass får ikonen för sin sparade sport; återimport av dubbletter ändrar inte sporten. Rekord beräknas för löpning, terränglöpning och virtuell löpning.

Importen behåller passnamn, beskrivning (kolumnen `Activity Description`), sport, starttid (UTC), distans i meter, tid och tillgängliga sammanfattningar från CSV. FIT och GPX, inklusive `.fit.gz` och `.gpx.gz`, ger tidsserier och rutter. Pass utan aktivitetsfil får en sammanfattning utan rutt. GPX-avstånd beräknas från GPS-punkterna; CSV-distansen behålls i sammanfattningen, så de kan skilja sig. Foton, kontakter, privata anteckningar och cyklar importeras inte. Detta använder användarens arkiv, utan anslutning till Stravas API.

Alla importerade pass är privata. Puls och de enskilda råfilerna sparas bara med samtycke; ZIP-arkivet sparas inte som råfil. Återkallat samtycke raderar puls och råfiler även från importerade pass. Importen fyller inte på puls i redan importerade pass: ta bort och importera dem igen om de först laddades upp utan samtycke.

Skor importeras före passen och kopplas med `Gear`-ID från `activities.csv`. Namn och märke hämtas från `shoes.csv` när dess namn kan matchas; annars används passets utrustningsnamn. Skor med olika Strava-ID hålls isär även om namnen är lika. `shoes.csv` saknar egna ID:n, så skor som inte används på något pass kan inte importeras säkert. Exporten saknar pensionsstatus: nya skor blir aktiva. Startdistans och starttid sätts till noll; statistik räknas från de kopplade passen, utan dubbelräkning. Redan importerade skors namn, startvärden och pensionering bevaras vid återimport. Återimport kompletterar tidigare pass som saknar utrustning, även dubbletter via filhash, men skriver inte över befintliga utrustningsval. Historiska pass får kopplas till pensionerade skor.

Efteråt visas antal importerade pass, skor, kompletterade skokopplingar och dubbletter samt Strava-ID för pass som misslyckats (till exempel en saknad, trasig eller ej stödd aktivitetsfil). Tidigare sparade pass behålls om ett senare pass misslyckas eller anslutningen bryts. Kör samma export igen för att fortsätta; Strava-ID och identiska aktivitetsfiler förhindrar dubbletter inom kontot. Passlistan visar 25 pass åt gången; resten nås med Visa fler.

API: `POST /api/activities/import/strava`, multipart-fält `file`, med inloggning och CSRF-token. Svar: en NDJSON-ström (`application/x-ndjson`) i samma anrop med `{ processed, total, imported, duplicates, failed, result }`. Status skickas vid start och därefter högst fyra gånger per sekund mellan passen. Sista meddelandet har `result: { imported, duplicates, failedActivityIds, equipmentImported, equipmentLinked }`; tidigare meddelanden har `result: null`. En ström utan slutresultat räknas som avbruten import. Valideringsfel före importen returneras fortfarande som vanliga JSON-felsvar. Högst 20 000 pass, 100 000 ZIP-poster, 20 MB per uppackad fil och totalt 1 GiB uppackade aktivitetsfiler per anrop, inklusive lästa byte från filer som avvisas. Budgeten räknas under läsningen; när den tar slut hoppas resterande pass över och rapporteras som misslyckade. Enskilda fel hindrar inte andra pass; ett ogiltigt CSV-manifest avvisar hela arkivet före lagring.

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

**Automatisk deploy:** `.github/workflows/deploy.yml` kör frontendkontroller och
API-tester på exakt den commit som pushats till `main` (inklusive merge av en PR).
När båda lyckas bygger Fly samma commits `Dockerfile` och deployar till `kuta`
med `--remote-only --ha=false`. Flys hälsokontroll på `/api/health` kontrollerar
att appen kommer upp; migreringarna körs vid start. Bara en deploy körs åt gången,
en pågående deploy avbryts inte och commits som inte längre är toppen av `main`
hoppas över. Även en push med enbart dokumentation kör kontroller och deploy.

Engångsuppsättning för CI:

1. Skapa en appbegränsad Fly-deploytoken för `kuta`, med namn `github-actions-kuta`
   och exempelvis ett års giltighet (`fly tokens create deploy --app kuta
   --name github-actions-kuta --expiry 8760h`). För över värdet direkt till
   GitHub Actions utan att skriva det i filer, kommandon eller chatt.
2. Spara hela token som GitHub Actions-secret `FLY_API_TOKEN` för
   `simonauner/kuta`, antingen som repository-secret eller som environment-secret
   i `production`. CI behöver bara denna token; appens databas-, lagrings- och
   certifikathemligheter ligger kvar hos Fly och hanteras via 1Password som tidigare.
3. Begränsa environment `production` till grenen `main`. Lämna required reviewers
   avstängt för automatisk deploy utan manuellt godkännande. Skydda `main` mot
   direktpush om alla produktionsändringar ska gå via PR.

Rotera Fly-token före utgången genom att uppdatera GitHub-secreten. Den ges bara
till deploysteget; frontend- och API-jobben får inga produktionshemligheter.
Saknad eller utgången token stoppar deployen. Upplägget följer
[Flys CI-guide](https://fly.io/docs/launch/continuous-deployment-with-github-actions/).

**Manuell deploy:** kör en maskin tills migreringar och token-förnyelse klarar flera maskiner, enligt `AGENTS.md`.

```bash
fly deploy --ha=false
```

## Integritet: underlag och öppna frågor

Arbetsunderlag för `web/privacy.html`, kontrollerat 2026-10-31. Skilj uppgifter som Simon har bekräftat, kodgranskning, leverantörers dokumentation och återstående frågor. Policyn är fortfarande ett utkast; denna lista innebär inget godkännande av publicering eller driftändringar.

### Bekräftat och beslutat

- Personuppgiftsansvarig: **Hack ska du ha AB, 559369-2287**. Simon Aunér är kontaktperson.
- `privacy@kutaapp.com` fungerar. Simon bekräftar kedjan **Cloudflare Email Routing → privat Gmail**, inte Google Workspace. Både Cloudflare och Google behandlar därför inkommande ärenden; Scaleway skickar kontomejl och är inte leverantör för inkorgen.
- Simon uppger att Kuta ännu inte har någon egen backup. Det fastställer inte vilka kopior leverantörerna behåller.
- Simon vill kunna genomföra **export och kontoradering inom sju dagar från begäran**. Det är målet för rutinen, inte en implementerad eller verifierad funktion. Kontoradering av autentisering, appdata och råfiler finns i Inställningar och är verifierad lokalt med syntetiska uppgifter. Fullständig export och hantering av relevanta leverantörsposter måste fortfarande förberedas och testas. Policyn får inte lova att alla leverantörsloggar och kopior försvinner inom samma tid.
- Lagringstider för supportmejl och avslutade/utgångna sessionsrader är **inte beslutade**. Förslagen är 12 månader efter avslutat supportärende respektive senast 30 dagar efter avslut/utgång; de ska inte beskrivas som befintliga rutiner.

### Vad koden visar om loggar och sessioner

Granskat: `api/Kuta.Api/Program.cs`, `PrivateLogProvider.cs`, `LogException.cs`, `appsettings.json`, `SupabaseAuthClient.cs`, `SupabaseCookieSession.cs`, `ActivityStorage.cs`, `RawFileStore.cs`, auth-/aktivitetsendpoints och `web/src/lib/api.ts`.

- Kutas applikationsloggning är begränsad till granskade strukturerade driftfält och rensad undantagsmetadata; se [Applikationsloggar och integritet](#applikationsloggar-och-integritet) för tillåtna fält och testade gränser. Automatisk HTTP-klientloggning och DbUp:s konsolloggning släpps inte igenom. Ingen generell HTTP-body-loggning eller extern felrapportering är konfigurerad.
- Detta beskriver repots kod. Faktiskt innehåll i hostade loggar, historiska loggar och eventuella driftöverstyrningar är inte kontrollerade; kodändringen måste driftsättas för att gälla nya loggar i drift.
- Besökarens IP skickas avsiktligt till Supabase Auth med `Sb-Forwarded-For` för rate limiting. Supabases egna auth-loggar kan innehålla användar-id, IP, user-agent, tidpunkt och händelse; de är separata från Kutas loggning ([Auth Audit Logs](https://supabase.com/docs/guides/auth/audit-logs)).
- `user_sessions` sparar användar-id, slumpat sessions-id, skapandetid, utgångstid och avslutstid. Utloggning sätter `ended_at`; automatisk städning saknas. Inloggningens 30 dagars inaktivitet/180 dagars maxgräns är inte en lagringstid för raderna.
- Filnycklar innehåller användar-id och filhash. Sådana identifierare kan finnas i leverantörernas lagringsloggar även utan att själva filinnehållet loggas.

### Avtalsunderlag från Kredovis

Simon bekräftar samma leverantörer och avtal för Kuta. Underlaget är Kredovis `docs/privacy.md`, avsnitt **DPA checklist**, och `docs/transfer-assessment.md` i projektet `bokforing`. Det är dokumentation av tidigare operatörsbekräftelser och avtalsgranskning; signerade original öppnades inte i Kuta-genomgången. Kredovis särskilda systemskydd och riskbedömningar ska inte automatiskt tillskrivas Kuta.

| Leverantör | Dokumenterat underlag | Vad återstår för Kuta? |
| --- | --- | --- |
| Supabase | Bolagets organisation/projekt och sparad DPA bekräftade 2026-10-12. DPA v1, 2026-08-01; SCC:er för överföringar, med Singapore som importör och möjlig åtkomst/loggbehandling i USA enligt Kredovis överföringsunderlag. | Bekräfta att Kutas projekt ligger under samma avtal. Anpassa beskrivningen av ändamål, pulsdata och skydd till Kuta. Kontrollera faktisk plan, loggtider, auth-auditlagring och leverantörsbackup. Projektmetadata kunde inte läsas genom Supabase-anslutningen på grund av saknad behörighet. |
| Fly.io | DPA signerad för bolaget 2026-10-12, avtalskopia granskad i Kredovis 2026-10-22. Underlaget anger DPF och SCC:er, möjlig amerikansk support/loggbehandling samt att signerad bilaga inte beskriver avsiktliga känsliga uppgifter. | Kuta behandlar avsiktligt puls/hälsodata: kontrollera och komplettera bilaga I.B för den användningen. Fly anger att känsliga kategorier ska anges där; inget generellt förbud ([COMPLIANCE-27–28](https://fly.io/legal/security-questionnaire/)). Kontrollera samma bolagsorganisation och vilka tjänster som används. |
| Scaleway | Bolagskonto, DPA, TEM-villkor och prenumeration på underbiträdesändringar bekräftade 2026-10-13. EU/Paris enligt leverantörens underlag. | Beskriv Kutas kontomejl, faktiska leverantörstider och hur enskilda mejlposter begärs raderade. Kredovis använder även AI-tjänster; det gör inte Kuta. |
| Cloudflare | Kredovis dokumenterar bolagets self-servicekonto. DPA ingår i self-serviceavtalet ([Cloudflare](https://www.cloudflare.com/trust-hub/gdpr/)). | Bekräfta att Kutas Email Routing ligger på bolagets konto och omfattas av avtalet. Kontrollera routningens köer, loggtider, underbiträden och behandlingsländer; Kredovis bedömning av Turnstile/Workers ersätter inte detta. |
| Google | Simon bekräftar privat Gmail som slutlig inkorg. Google anger att konsument-Gmail saknar biträdesavtal och att Google inte är biträde för den tjänsten ([Privacy Help Center](https://support.google.com/policies/answer/9581826/privacy-help-center)). | Välj företagsinkorg med tillämpligt biträdesavtal, till exempel Workspace, eller utred en annan hållbar avtalslösning. Kontrollera lagring, överföringar och hantering av redan mottagna ärenden. Ingen mejlflytt eller avtalsaccept är gjord. |

### Leverantörernas lagring: känt och okänt

- **Fly.io:** sökbara applikationsloggar sparas sju dagar enligt [Logging overview](https://docs.fly.io/monitoring/logging-overview). Detta fastställer inte alla interna säkerhets-/supportloggars tider eller lagringsländer.
- **Scaleway TEM:** enligt [dataskyddssidan](https://www.scaleway.com/en/docs/transactional-email/reference-content/data-privacy/) raderas normalt mejlinnehåll när mottagarservern accepterar mejlet, vid permanent leveransfel eller efter 36 timmars misslyckade försök. Undantag: spam 72 timmar och blockerad domän 15 dagar; krypterade säkerhetskopior sju dagar. Avsändare, mottagare, ämne, avsändande IP, leveransstatus och tidsstämplar kan finnas i metadata/loggar. De behålls medan TEM-domänen är aktiv. Enskilda poster/adresser/loggar kan begäras raderade via support; ingen tidsfrist för sådan radering har verifierats.
- **Supabase:** logg- och backuptider måste kontrolleras för Kutas faktiska plan och inställningar. Auth-auditposter i databasen behöver bedömas separat från dashboardloggar. [Backupdokumentationen](https://supabase.com/docs/guides/platform/backups) beskriver leverantörsbackuper; avsaknad av egen backup är inte bevis för att leverantörskopior saknas.
- **Gmail och Cloudflare Email Routing:** faktiska tider för inkorg, papperskorg, återställningskopior, routningsköer och loggar är inte verifierade. Lova inte EU-/Sverige-exklusiv behandling.

### Kvar att göra

- [ ] Välj/ordna företagsinkorg och dokumentera avtal, åtkomst, överföringar och hantering av befintliga mejl. Kontrollera Cloudflare Email Routing enligt tabellen ovan.
- [ ] Kontrollera Kutas Supabase-plan, loggtider, auditlagring, leverantörsbackuper och faktisk avtalsanknytning.
- [ ] Komplettera/verifiera Fly-avtalets beskrivning av avsiktlig hälsodatabehandling och Kutas överföringsskydd.
- [ ] Besluta lagringstid för supportärenden och sessionsrader; inför och verifiera motsvarande radering.
- [ ] Förbered och testa export och kontoradering med syntetiska uppgifter: autentisering, profil, utrustning, pass, puls, råfiler, sessioner och relevanta leverantörsposter. Kontrollera två användare så att fel persons uppgifter inte exporteras/raderas. Bestäm säker leverans av exporten och dokumentera eventuella lagliga undantag/kvarvarande kopior.
- [ ] Beskriv sju dagar som faktisk normal tidsfrist först när rutinen fungerar. GDPR kräver hantering utan onödigt dröjsmål, normalt inom en månad ([IMY](https://www.imy.se/privatperson/dataskydd/dina-rattigheter/radering/)); tidsfristen ersätter inte själva skyldigheten.
- [ ] Ersätt öppna utkasttexter i båda språkversionerna med verifierade uppgifter före publicering. Uppdatera igen innan automatisk Garmininsamling eller annan ny behandling införs.
