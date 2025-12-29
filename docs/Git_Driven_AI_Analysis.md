# Git-Driven AI Workflow Analysis

## 1. Core Concept: The "Async Colleague" Model
Instead of a "Chatbot" that just replies text, the AI acts as a remote colleague who:
1.  Clones your project.
2.  Creates a feature branch.
3.  Does the work (edits files).
4.  Submits a "Pull Request" (Proposal).
5.  Waits for you to Merge.

## 2. Hypothetical Scenarios & Feature Derivation

### Scenario A: The "Parallel Work" (Non-Blocking)
**Context**: User is manually adjusting the Living Room sofa. User asks AI: "Design the Master Bedroom."
**Workflow**:
1.  User continues working on `main` branch (Living Room).
2.  AI creates branch `feat/ai-bedroom-design` from `main`.
3.  AI generates `schemes/default/modules.json` updates for the Bedroom zone.
4.  AI commits changes: `git commit -m "Design Master Bedroom with King Bed"`
5.  Server notifies User: "Bedroom Design Ready".
6.  User enters "Review Mode" (Visual Diff).
7.  User accepts changes. Server performs `git merge`.

**-> Derived AI Features**:
- **Branch Management**: Ability to create, switch, and delete branches.
- **Scope Isolation**: AI must ensure its edits are strictly confined to the requested "Zone" to avoid conflicts with User's parallel work in other zones.
- **Semantic Commits**: AI should generate readable commit messages explaining *why* it did what it did (e.g., "Placed bed against solid wall to avoid window").

### Scenario B: The "Multi-Option Proposal"
**Context**: User asks: "Give me 3 different layouts for the Dining Room."
**Workflow**:
1.  AI creates 3 branches: `feat/ai-dining-opt1`, `feat/ai-dining-opt2`, `feat/ai-dining-opt3`.
2.  AI generates different layouts in each branch.
3.  Web UI presents a "Carousel" or "Grid View" of the 3 options (rendering data from the 3 branches).
4.  User clicks "Option 2".
5.  System merges `feat/ai-dining-opt2` into `main`.

**-> Derived AI Features**:
- **Strategy Variation**: AI needs parameters to generate *distinct* strategies (e.g., "Maximize Seating" vs "Maximize Flow").
- **Parallel Execution**: Ability to manage multiple working contexts (branches) simultaneously or sequentially.

### Scenario C: The "Conflict Resolution"
**Context**: User deletes a wall in `main` while AI is placing furniture against that wall in `feat/ai-design`.
**Workflow**:
1.  AI finishes design on old map data.
2.  User tries to merge AI branch.
3.  **Visual Conflict**: The bed is now floating in the air (because the wall is gone).
4.  System detects "Spatial Conflict" (not just text conflict).
5.  System asks AI to "Rebase and Fix".
6.  AI checks out `feat/ai-design`, rebases on new `main`, detects the invalid placement, moves the bed, updates commit.

**-> Derived AI Features**:
- **Rebase & Repair**: Ability to update a PR based on new main branch changes.
- **Self-Correction**: Re-running validation logic after a rebase.

## 3. Required AI Capabilities (The "Git Toolset")

To support this, the AI needs a specific set of MCP Tools beyond just "Write File":

| Feature Category | Specific Tool / Capability | Purpose |
| :--- | :--- | :--- |
| **Version Control** | `git_create_branch(name, base)` | Start a new task |
| | `git_commit(message)` | Save a checkpoint of work |
| | `git_log(file_path)` | Understand history of a zone |
| | `git_diff(branch_a, branch_b)` | See what changed |
| **Context** | `get_current_branch()` | Know where I am working |
| **Collaboration** | `create_pull_request(title, desc)` | Signal completion |

## 4. The "Visual Merge" UI
This is the critical human interface. It's not a text diff.
- **Zone-Based Diff**: "Zone A changed in Main", "Zone B changed in AI Branch".
- **Selective Merge**: Checkboxes for "Keep my Living Room" + "Accept AI's Bedroom".
