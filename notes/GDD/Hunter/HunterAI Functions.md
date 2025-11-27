|**Section**|**Variable / Function**|**Purpose & State Management**|
|---|---|---|
|**New Memory**|`currentInterestTarget` (Transform)|**NEW:** The object the Hunter wants to check (Door, Table, etc.). _Data Anchor._|
||`currentNavDestination` (Vector3)|**NEW:** The floor coordinate (Vantage Point) calculated by the solver. _Movement Target._|
||`Queue<HunterJob> jobQueue`|**NEW:** The short-term memory of tasks (the HTN plan). Cleared on interrupt.|
||`float Frustration, Caution`|**NEW:** Utility variables tracking the Hunter's internal state.|
|**Scoring Logic**|`GetNextTargetScore()`|**Simplified:** Only handles standard logic and final scoring. Filters out targets where `IsDoorOnCooldown` is TRUE.|
|**Navigation Helpers**|`GetValidFloorPosition()`|**Consolidated:** Handles `SamplePosition` and the vertical lift fix. Used by all movement logic.|
|**Room History**|`currentRoomInfo`, `previousRoomInfo`|**KEEP:** Tracks room transitions to prevent immediate backtracking.|