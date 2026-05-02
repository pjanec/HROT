v tankovém četě obsahuje čtyři tanky, ta má svého virtuálního velitele a tanky mají za úkol útočit na nějaký cíl přes vrchol kopce. To znamená, že tank musí vyjet na vrchol kopce až tak daleko, dokud neuvidí dobře na cíl. Odtamtuď musí vystřelit na cíl a potom se zase vrátit na počáteční místo. Velitel na začátku potřebuje znát palebnou čáru, která se nachází na vrcholu kopce, a určí na této palebné čáře místa, odkud mohou jednotlivé tanky útočit. Poté nechá tanky útočit tak dlouho, dokud cíl není zničen. Tankům vždycky řekne dojeď na konkrétní místo na palebné čáře, zautoč a vrať se zoět, abys nebyl exponován. Tato operace dojeď na místo, zautoč a vrať se, je samostatná doktrína, kterou každý tank dokáže vykonávat samostatně. Tank jede na palebnou čáru a klidně i dál, dokud na víl neuvidí. Velitelova doktrína začíná přípravou - nalezením bodů na palebné čáře s vhodnými rozestupy. Poté nechá tanky útočit, a pokaždé změní palebné místo. Až je cíl zničen, tanky se vrátí do základní pozice a mise (velitelova doktrína) končí. Jak na to? Btree/hsm? Yzly? Akce? Podmínky? Události?

1) Velitelská doktrína čety
   - plánuje
   - přiděluje palebná místa
   - řídí opakování útoků
   - sleduje stav cíle a reporty tanků

2) Členská tanková doktrína
   - dojeď k palebnému místu / za horizont
   - najdi viditelnost na cíl
   - vystřel
   - couvni / vrať se do krytu
   - skonči


Velitel: spíš BTree než HSM

Velitelská logika je sekvenční, plánovací a opakující se:

Příprava
  → najdi palebnou čáru
  → spočti sloty pro 4 tanky
  → inicializuj skupinový stav

Hlavní smyčka
  → pokud cíl žije
      → pro každý dostupný tank vyber nové palebné místo
      → pošli TacticalOrderChannel
      → čekej na reporty / dokončení útoku
  → pokud cíl zničen
      → pošli návrat / idle
      → skonči doktrínu
      
      
      
Tohle je přesně případ pro FastBTree, protože podle GUIDE je BTree vhodný pro komplexní sekvenční chování a vícefázový boj .

Velitelova BTree by mohla vypadat koncepčně takto:

PlatoonHillAttack_BT

Sequence
  Action_PrepareFiringLine
  Action_AssignInitialSlots

  RepeatUntil(TargetDestroyed)
    Sequence
      Action_AssignOrdersToAvailableTanks
      Action_MonitorTankReports
      Action_ReassignCompletedTanks

  Action_OrderAllTanksReturn
  Condition_AllTanksSafe
  
  
Tank: spíš BTree, případně vnitřně HSM

Členská doktrína „vyjeď–vystřel–vrať se“ je také sekvenční:

HullDownAttackRun_BT

Sequence
  Action_MoveTowardAssignedFirePoint
  Selector
    Condition_TargetVisible
    Action_CreepForwardUntilVisible
  Action_StopOrStabilize
  Action_FireAtTarget
  Action_ReverseToCoveredPosition
  Action_ReportAttackRunFinished
  
  
  Velitel by tankům nepřiřazoval přímo waypointy do locomotion channelu. Velitel má psát vyšší taktický záměr.
  
  
Reporty tanků

Každý tank by měl mít komponentu:

public struct TacticalReport
{
    public uint LastOrderInstanceId;

    public byte State;        // Idle, MovingToFirePoint, Firing, Reversing, Done, Failed
    public byte HasLineOfSight;
    public byte Fired;
    public byte UnderFire;

    public float LastKnownTargetVisibility;
}

  
  
  
Tanková doktrína ji průběžně zapisuje. Velitel ji čte z ECS. Neposílal bych reporty jako událost každým framem; komponenta je lepší pro stav. Událost použij jen pro hrany, například „order completed“ nebo „target destroyed“.

Události

OrderAssignedEvent
AttackRunCompletedEvent
TargetDestroyedEvent
TankUnableToAttackEvent

Kontinuální stav nech v komponentách:

TacticalReport
GroupMember
GroupTacticalState
TacticalOrderChannel


To odpovídá GUIDE: AI akce a podmínky mohou číst ECS stav a zapisovat intenty do kanálů, zatímco fyzické vykonání řeší muscle tier přes dispatchery .

Výsledná architektura

PlatoonCommander entity
  Behavior: PlatoonHillAttack_BT
  Components:
    GroupTacticalState
    FiringLinePlan
    PlatoonMemberList

Tank entities
  Components:
    TacticalOrderChannel
    TacticalReport
    GroupMember
    LocomotionChannel
    WeaponChannel

Systems:
  TacticalOrderDispatcherSystem
    TacticalOrderChannel → AssignBehaviorEvent

  BehaviorIngressSystem
    spustí konkrétní tankovou doktrínu

  BTreeTickSystem
    tickuje velitelskou i tankové doktríny
    
    