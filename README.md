# InfoPC Tray

Applicazione portatile per Windows 11 che mostra, dall'area di notifica:

- nome del PC e utente;
- indirizzi IPv4 locali;
- indirizzo IP pubblico;
- porte TCP e UDP locali in ascolto.
- seconda pagina hardware aggiornata ogni 2 secondi con temperature CPU, GPU,
  RAM e dischi;
- descrizione, capacita', tipo e stato dei dischi quando disponibili;
- quantita', tipo, velocita' e memoria RAM disponibile.
- uno o due righelli sempre in primo piano, orizzontali o verticali;
- misura in pixel o millimetri, lunghezza e trasparenza regolabili;
- calibrazione DPI per rendere precisa la misura fisica sul monitor;
- posizione e impostazioni dei righelli salvate automaticamente.

## Righelli (v1.3.2)

Fare clic con il pulsante destro sull'icona IP vicino all'orologio e aprire il
menu `Righello`. Da qui si possono mostrare il righello 1, il righello 2 oppure
entrambi. I righelli si trascinano tenendo premuto il pulsante sinistro; con il
pulsante destro si possono ruotare, nascondere, cambiare l'unita' tra pixel e
millimetri e regolare immediatamente la trasparenza con un cursore.
La voce `Posizione dello zero` permette, separatamente per ciascun righello, di
partire da zero a sinistra/in alto oppure di collocare lo zero al centro, con
valori negativi da un lato e positivi dall'altro.

In `Impostazioni righelli` si scelgono unita' di misura, lunghezza, trasparenza,
orientamento e DPI calibrati. Per i millimetri reali confrontare la scala a
video con un righello fisico e correggere il valore DPI.

Il programma non richiede privilegi di amministratore. Le porte locali in ascolto non coincidono necessariamente con le porte inoltrate dal router verso Internet.

## Compilazione

La GitHub Action inclusa genera `InfoPC-Tray.exe` come applicazione Windows x64 autonoma e a file singolo.

Le temperature di CPU, RAM, GPU e dischi sono lette direttamente da InfoPC Tray
tramite LibreHardwareMonitor 0.9.6. Non serve avviare altri programmi. Per
accedere ai sensori hardware, Windows richiede la conferma amministratore UAC.

Se CPU, RAM o sensori della scheda madre non vengono visualizzati, premere
`Installa driver sensori` nella finestra del programma. InfoPC Tray installerà
PawnIO tramite Windows Package Manager. PawnIO è un driver e non è un secondo
programma da lasciare aperto. Al termine occorre chiudere completamente e
riavviare InfoPC Tray.
