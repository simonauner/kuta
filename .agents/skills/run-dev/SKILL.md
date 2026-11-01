---
name: run-dev
description: Starta Kuta lokalt med Supabase, API och Vite för att kontrollera flöden eller ta skärmbilder. Använd vid lokal körning och verifiering i webbläsaren.
---

# Kör Kuta lokalt

Läs [lokal körning i README](../../../README.md#köra-lokalt) och använd det befintliga [startskriptet](../../../scripts/run-dev.sh). Docker, Supabase CLI, .NET 10 och nvm med versionen i `.nvmrc` behövs. Skriptet installerar webbens beroenden om de saknas och väljer Node-version själv.

## Välj miljö

Kontrollera först om en lämplig miljö redan kör. En miljö som uppgiften inte skapat är lånad: återställ, stoppa eller ta inte bort den utan användarens uttryckliga instruktion.

För en ny verifieringsmiljö, välj en ledig portserie och ett Supabase-projekt som inte används av något annat worktree. Samma BAS ger samma projektnamn även i olika worktrees. Kontrollera portarna och befintliga Docker-containrar före start; håll serierna minst 30 portar isär.

```bash
scripts/run-dev.sh --ports 55400
```

Exemplet använder webben på 55400, API:t på 55402 och Supabase på 55420–55429, med Mailpit på 55424. Standardkörning utan `--ports` använder 55300 som BAS och projektet `kuta`; andra serier använder `kuta-BAS` och `.context/supabase-BAS`.

Kör skriptet i en beständig terminal eller bakgrundssession. Anteckna worktreets absoluta sökväg, BAS, skriptets PID/session, Supabase-projektet och dess workdir samt om Supabase redan körde. De behövs för att skilja egna resurser från lånade.

## Kontrollera flödet

- Vänta tills både webben svarar och API:ts `/api/health` ger ett lyckat svar. Utskrivna adresser betyder inte att API:t är redo.
- Skapa vid behov ett syntetiskt konto genom appens registrering. Bekräftelsemejlet finns i instansens Mailpit. Återanvänd inloggningen; auth har en gräns på 10 anrop per 5 minuter och IP.
- Om mejlet använder Supabases engelska standardmall, kontrollera projektet och starta bara om auth-containern för en miljö som uppgiften äger: `docker restart supabase_auth_kuta-BAS` för en extra instans (ersätt BAS med portbasen). Registrera sedan igen. Ändra inte en lånad miljö för att lösa det.
- Vite laddar om webbändringar. Efter C#-ändringar måste skriptet startas om; det använder inte `dotnet watch`.
- Kontrollera berörda flöden, direktlänkar, bakåt/framåt, tangentbordsåtkomst samt relevanta laddnings- och feltillstånd. Läs konsol- och nätverksfel. Vid ändrade beroenden eller CSP ska även den byggda webben kontrolleras enligt `AGENTS.md`.
- För UI-/UX-PR: ta jämförbara före- och efterbilder med samma data, viewport och tema, och inkludera dem tydligt märkta i PR-beskrivningen via länkar eller bilagor som granskare kommer åt. Ta förebilden innan redigering och efterbilden av det verifierade resultatet. Beskriv den specifika begränsningen om någon bild inte kan tas eller bifogas.

Testerna följer med till den egna portserien med samma bas: `KUTA_PORTS=BAS dotnet test --project api/Kuta.Api.Tests` (Supabase, Storage och Mailpit från BAS+20–29). Utan `KUTA_PORTS` används standardinstansen. En lyckad webbläsarkontroll på den egna portserien innebär inte att integrationstesterna har körts där.

## Avsluta

Stoppa bara skriptet som uppgiften startade, med Ctrl+C eller dess antecknade PID/session. Skriptet stoppar API och Vite men lämnar Supabase igång. Stoppa även Supabase om uppgiften startade den och användaren inte vill behålla miljön. När sessionen avslutas eller arkiveras ska den egna Supabase-instansen tas bort helt, inklusive Docker-volymer och workdir. Kör från det antecknade worktreet med rätt BAS, exempelvis:

```bash
supabase stop --no-backup --workdir .context/supabase-55400
```

```bash
rm -rf .context/supabase-55400
```

Kontrollera efteråt med `docker volume ls --filter label=com.supabase.cli.project=kuta-55400` att inga volymer finns kvar, och ta bort eventuella rester med `docker volume rm`. Detta gäller bara instansen som uppgiften startade. Använd inte globala Docker-prune-kommandon, och lämna lånade resurser och standardinstansen `kuta` orörda. Om användaren vill behålla miljön, lämna dess URL och uppgifterna som behövs för att stoppa den senare.

Rapportera kontrollerade flöden, resultat och eventuella hinder, att data var syntetiska och om en miljö lämnats igång.
