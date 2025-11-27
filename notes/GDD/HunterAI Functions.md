|**Variable / Enum**|**Type**|**Description**|
|---|---|---|
|`JobType`|Enum|**{ `MoveToVantage`, `Peek`, `Interact`, `Wait`, `GotoZone` }**. Defines the primitive action.|
|`targetTransform`|`Transform`|The object of the job (The actual interest point).|
|`vantagePosition`|`Vector3`|The pre-calculated _floor position_ to walk to (where LOS is clear).|
|`duration`|`float`|Time to spend on this task (e.g., 2.0s for Peeking).|