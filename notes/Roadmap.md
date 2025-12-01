


# 🗺️ Hunter AI Stalker Roadmap

**Goal:** Transition the AI from a Reactive Prowler (Waypoint-to-Waypoint) to a Utility-Driven Stalker (Vantage-to-Vantage). This decouples the Hunter's **Movement** from its **Target of Interest** to simulate observation and stalking.

## Phase 1: Foundation & Decoupling (The Motor Upgrade)

### Step 1: Code Audit & Data Split (The Decoupling)
**Objective:** Consolidate navigation helpers and split the destination variable to support Vantage Point logic.
- [x] **Code Cleanup (HunterAI.cs):** Consolidate all existing scattered `NavMesh.CalculatePath` and `SamplePosition` calls into one robust, central **`NavigationHelper`** function (The Audit Fix).
- [x] **Refactor `HunterAI.cs`**:
    - Remove `public Transform currentPatrolTarget`.
    - Add `public Transform currentInterestTarget` (The Data Anchor: Table, Door, Vent).
    - Add `public Vector3 currentNavDestination` (The Floor Coordinate for the NavMeshAgent).
- [x] **Update Nodes**: Modify `AcquirePatrolTarget` and `MoveToPatrolPoint` to use the new `currentInterestTarget` and `currentNavDestination` variables.

### Step 2: The Vantage Solver (The Stalking Brain)
**Objective:** Give the Hunter the ability to calculate a "viewing spot" dynamically.
- [x] **Create `VantageSolver.cs`** (Static Helper):
    - Function: `GetVantagePosition(Transform targetInterestObject)`.
    - Algorithm: Calculate a spot (e.g., 3m away, clear Line of Sight, NavMesh snapped).
- [x] **Update `AcquirePatrolTarget`**:
    - Logic: Use `GetVantagePosition` to calculate the `currentNavDestination` *before* moving.

## Phase 2: Planning & Execution (The Job System)

### Step 3: The Job Class & Queue (Short-Term Memory)
**Objective:** Create the structure for chained plans that can be aborted.
- [x] **Create `HunterJob.cs`** (Non-MonoBehaviour Class):
    - Enum `JobType`: { `MoveToVantage`, `Peek`, `Interact`, `Wait`, `Chase` }.
    - Variables: `Target`, `Duration`, `Type`.
- [x] **Update `HunterAI.cs`**:
    - Add `Queue<HunterJob> jobQueue`.
    - Add basic queue methods (`AddJob()`, `GetNextJob()`, `ClearJobs()`).

### Step 4: The Job Generator (The HTN Domain)
**Objective:** Write the "Recipes" for complex actions (the HTN decomposition).
- [ ] **Create `JobGenerator.cs`**:
    - Function: `GenerateRoomClear(RoomInfo room)`.
    - Logic: Fills the `jobQueue` with a sequence of tasks (e.g., Doorway Peek $\to$ Move to Center Vantage $\to$ Scan).
- [ ] **Rewrite `AcquirePatrolTarget` (ExecuteNextJob):**
    - Logic: If `jobQueue` is empty, call `JobGenerator.GenerateRoomClear(bestRoom)`.
    - Logic: Pop the next job and assign its data to `currentInterestTarget` and `currentNavDestination`.
- [ ] **Update ```Creeping``` in Doorway job so it works. 
	- It needs re-designing

Things to Keep In mind
```
While the plan is solid, here are the potential pitfalls to watch out for:

- **Mistake 1: Over-Engineering the Job System**
    
    - _Risk:_ Building a full, generic HTN solver (like _Killzone_'s) is a massive undertaking.
        
    - _Correction:_ Stick to a **Domain-Specific HTN**. Don't write a generic solver that can solve _any_ problem. Write a simple `JobGenerator` that knows specifically how to clear rooms. You don't need a recursive planner yet; a simple "Recipe" system is enough.
        
- **Mistake 2: Syncing Data**
    
    - _Risk:_ The Brain (Planner) generates a plan based on the world state at Time A. By the time the Body executes Step 3 (Time B), the world might have changed (e.g., door closed, player moved).
        
    - _Correction:_ Ensure every Job has a **Pre-Condition Check**. Before executing a job from the queue, the Behavior Tree must ask: "Is this still valid?" (e.g., Is the door still there? Is the room still hot?).
        
- **Mistake 3: Utility vs. Planning Conflict**
    
    - _Risk:_ If the Utility system (Moods) changes mid-plan (e.g., Frustration spikes), does it abort the current plan?
        
    - _Correction:_ Decide on an interrupt rule. Usually, you finish the current _atomic_ job (e.g., finish the Peek) before switching strategies, unless it's a high-priority interrupt (Chase).
```

## Phase 3: Personality & Pacing (Utility & Director)

### Step 5: Utility Stats (The Moods)
**Objective:** Add variables that control the *style* of the plan generated.
- [ ] **Update `HunterAI.cs`**:
    - Add `float Frustration` (for repeated search failures).
    - Add `float Caution` (for stealthy approach decisions).
    - Add `float Aggression` (for combat readiness).
- [ ] **Update Event Handlers:** Increment these stats based on player interaction (e.g., missing the player increases Frustration).

### Step 6: Utility Logic (The Variation)
**Objective:** Make the plan unpredictable.
- [ ] **Update `JobGenerator`**:
    - Use the new Moods (Frustration, Caution) as **parameters** when generating the job chain.
    - *Example:* If `Frustration > 70`, switch the plan to `AggressiveSearch` (skip all 'Vantage' jobs, set movement speed to high, add 'KickDoor' interaction).

### Step 7: The Director Integration (The Conductor)
**Objective:** Use the AI's internal state to control the game's tension curve.
- [ ] **Update `Director.cs`**:
    - Implement the "Tension Bucket" or "Pacing Curve" logic.
    - **New Action:** Director calls a Hunter function that forces a "Release" job (e.g., `HunterAI.StartLongRest()`) if Tension is too high, overriding the current plan.

## 🧠 Gemini Notes: Architectural Guard Rails

**1. The "Action Atomic" Rule**
* **Mistake:** We tried to make `PerformDoorwayPeek` handle movement logic (Creep), which fought the NavMesh and caused jittering.
* **Rule:** **Actions must NOT move the Agent.**
    * `Move` Jobs handle *Positioning*.
    * `Action` Jobs handle *Animation/Wait*.
    * **Never** combine them. If the Hunter needs to "Creep," that is a `MoveTo` job with `speed=0.5`, followed by a `Peek` job.

**2. The "Memory Prediction" Rule**
* **Mistake:** We tried to force the Hunter to enter a room based on "Commitment," even if his sensors told him it was empty.
* **Rule:** **The Planner predicts; The Sensor corrects.**
    * The HTN generates a chain: `Peek -> Enter -> Search`.
    * Each step must validate the *current* memory before starting.
    * If `Peek` cools the room heat to 0, the `Enter` job must be auto-aborted by the Executor before it starts.

**3. The "Vantage" Rule**
* **Mistake:** Walking to the exact coordinate of a patrol point (inside a door frame) caused physics clipping and "Wall Staring."
* **Rule:** **Never path to an object.**
    * Always path to a `VantagePosition` calculated by the Solver.
    * The `TargetOfInterest` is for the Head (Looking), not the Body (Walking).

**4. The "Director" Rule**
* **Mistake:** Making the Director control the Hunter's legs.
* **Rule:** **The Director controls Mood, not Motor.**
    * Director: "Be Aggressive."
    * Hunter Brain: "Okay, I will generate 'Kick Door' jobs instead of 'Peek' jobs."