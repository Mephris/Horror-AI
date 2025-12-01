


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
- [ ] **Create `VantageSolver.cs`** (Static Helper):
    - Function: `GetVantagePosition(Transform targetInterestObject)`.
    - Algorithm: Calculate a spot (e.g., 3m away, clear Line of Sight, NavMesh snapped).
- [ ] **Update `AcquirePatrolTarget`**:
    - Logic: Use `GetVantagePosition` to calculate the `currentNavDestination` *before* moving.

## Phase 2: Planning & Execution (The Job System)

### Step 3: The Job Class & Queue (Short-Term Memory)
**Objective:** Create the structure for chained plans that can be aborted.
- [ ] **Create `HunterJob.cs`** (Non-MonoBehaviour Class):
    - Enum `JobType`: { `MoveToVantage`, `Peek`, `Interact`, `Wait`, `Chase` }.
    - Variables: `Target`, `Duration`, `Type`.
- [ ] **Update `HunterAI.cs`**:
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
