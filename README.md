# InfoPC Tray

Applicazione portatile per Windows 11 che mostra, dall'area di notifica:

- nome del PC e utente;
- indirizzi IPv4 locali;
- indirizzo IP pubblico;
- porte TCP e UDP locali in ascolto.
- seconda pagina hardware aggiornata ogni 2 secondi con temperature CPU, GPU,
  eventuale NPU, RAM e dischi;
- descrizione, capacita', tipo e stato dei dischi quando disponibili;
- quantita', tipo, velocita' e memoria RAM disponibile.

Il programma non richiede privilegi di amministratore. Le porte locali in ascolto non coincidono necessariamente con le porte inoltrate dal router verso Internet.

## Compilazione

La GitHub Action inclusa genera `InfoPC-Tray.exe` come applicazione Windows x64 autonoma e a file singolo.

Le temperature sono lette tramite LibreHardwareMonitor. Alcuni sensori, in
particolare NPU, RAM e determinati SSD, possono non essere esposti dal produttore.
