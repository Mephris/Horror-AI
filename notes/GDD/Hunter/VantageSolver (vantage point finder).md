### Core Concept
The Vantage Solver is a static utility engine that decouples interest (what AI wants to see) from **Navigation** (where AI stands). Instead of walking directly to an object's pivot point as it was before, the AI calculates a "tactical" observation position that ensures visibility while maintaining a safe/logical distance.

### The Logic Flow
The solver uses a **Candidate Generation** approach similiar to unreal engine's EQS (Enviroment Query System). It doesn't caluclate a single vantage point; it tests multiple options to find the one that fits the enviroment best. 

#### Step 1: Vector Calculation

- **Input:** `TargetPosition` (e.g., Table) and `HunterPosition`.
    
- **Logic:** Calculate the vector from the **Target $\to$ Hunter**.
    
- **Goal:** We want to back up along this line by `IdealDistance` (e.g., 3.0 meters). This ensures the Hunter stands _between_ the door and the object, or backs away from the object naturally, rather than walking past it to the other side. 

#### Step 2: Candidate Generation
To handle cluttered rooms (pillars, tables, weird corners), the solver generates **3 Candidate Points** in a semi-circle facing the Hunter:

1. **Center:** Directly backwards along the ideal vector (0°).
    
2. **Left Flank:** Rotated -45° around the target.
    
3. **Right Flank:** Rotated +45° around the target.

_Why:_ If the "Center" path is blocked by a pillar or wall, one of the 45° flank points will usually offer a clear line of sight around the obstacle.

#### Step 3: The Filters (Validation)

Each candidate point must pass two strict tests to be accepted:

1. **NavMesh Snap (Reachability):**
    
    - _Test:_ `NavMesh.SamplePosition(Candidate, 1.0m)`.
        
    - _Pass:_ The point is on valid, walkable "Blue Carpet."
        
    - _Fail:_ The point is inside a wall, over a void, or on a non-walkable prop.
        
2. **Raycast Visibility (Line of Sight):**
    
    - _Test:_ Physics Raycast from `Candidate + EyeHeight` $\to$ `Target + CenterOffset`.
        
    - _Pass:_ The ray hits the Target without hitting a wall (`Default` layer) first.
        
    - _Fail:_ A wall or obstacle blocks the view.

#### Step 4: Selection & Fallback

- **Selection:** The first Candidate (Center $\to$ Left $\to$ Right) that passes both filters is returned immediately.
    
- **Recursive Fallback:** If **ALL** 3.0m candidates fail (e.g., the room is tiny), the solver re-runs the entire logic with a smaller distance (**`IdealDistance/2.0f**).
    
- **Final Fallback:** If even the 1.5m check fails, it returns the **Target's actual position**. (The Hunter is forced to walk directly to the object as a last resort).


### In-Game Behavior (The Result)

- **In Open Space:** The Hunter stops 3 meters away from the table, looking professional and observant.
    
- **Around Corners:** The Hunter naturally "slices the pie," picking a vantage point that allows him to see around the doorframe without exposing himself fully.
    
- **In Clutter:** The Hunter steps to the side (45°) to look around a pillar blocking his view.

