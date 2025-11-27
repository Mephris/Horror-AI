## Heat vs Interest

The "Intelligence" in your system is the final **Utility Score** calculated by the Hunter's brain. This score is a calculation combining the objective risk (Heat) with the subjective efficiency (Interest).

|**Current Variable Set**|**Role in Utility AI**|**Description**|
|---|---|---|
|**Heat** (`playerProbability`, `generalCuriosity`)|**Reward Value**|The likelihood of success (finding the player).|
|**Distance Penalty**|**Cost Value**|The physical cost of the action (time/effort).|
|**Interest Multipliers** (`sameRoomMultiplier`, etc.)|**Efficiency/Tactical Score**|How "smart" the action is relative to the current context.|

### The Integration into the Roadmap

In **Phase 3: Utility & Variation** of our roadmap, we will expand this layer by adding "Mood" variables (`Frustration`, `Caution`) to the calculation.

When that phase begins, your Utility Function will become:

$$\text{Final Utility} = \text{Base Score} \times \text{Momentum} \times \text{Tactical Bonus} \times \text{Mood Factor} - \text{Cost}$$

By keeping the Interest logic tied to the score, you ensure that the Hunter's **decisions are always optimized for both risk (Heat) and efficiency (Interest).**