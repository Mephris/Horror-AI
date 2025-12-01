### Core Concept
The Vantage Solver is a static utility engine that decouples interest (what AI wants to see) from **Navigation** (where AI stands). Instead of walking directly to an object's pivot point as it was before, the AI calculates a "tactical" observation position that ensures visibility while maintaining a safe/logical distance.

### The Logic Flow
The solver uses a **Candidate Generation** approach similiar to unreal engine's EQS (Enviroment Query System). It doesn't caluclate a single vantage point; it tests multiple options to find the one that fits the enviroment best. 

#### Step 1: Vector Calculation
- **Input:** `TargetPosition` (e.g., Table) and `HunterPosition`.
    
- **Logic:** Calculate the vector from the **Target $\to$ Hunter**.
- 
- **Goal:** We want to back up along this line by `IdealDistance` (e.g., 3.0 meters). This ensures the Hunter stands _between_ the door and the object, or backs away from the object naturally, rather than walking past it to the other side.