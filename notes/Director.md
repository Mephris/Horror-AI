An overseer ai which sends out commands to the hunter and controls events. 

Currently Director is able to send out commands to the Hunter in order to manipulate their decisionmaking by changing the base player probability for each patrol point that the Hunter has in its memory. 


##### Director Changelog 
```Director Changelog
- **Direct Command Nodes Deleted:** The `IsHPCommandTarget` condition and `ExecuteHPCommand` task were removed from the BT.
    
- **Event Handlers Converted:** The `OnCommandToMove` and `OnHighPriorityCommandToMove` event subscriptions in **`Hunter_Basic.cs`** no longer tell the Hunter where to go. Instead, they call `ModifyMemoryNearLocation` to:
    
    - Increase a Patrol Point's **`playerProbability`** (e.g., by 0.2 or 0.5).
        
    - Set the **`hasDirectorTip`** tag.
        
- **Organic Movement:** The **`MoveToPatrolPoint`** task naturally selects the now high-scoring, commanded location, making the AI's response feel organic rather than forced.
```

Currently Director is a state machine and akin to Hunter will go through a revamp, what we can do is to make him a "Goal oriented brain" rather then the [[Hunter| hunter's]] hybrid approach. 

Idea is to get the high level planning from the [[Director|cheater]] to be perceived by the Hunter. Hunter then will do his own short term planning. 