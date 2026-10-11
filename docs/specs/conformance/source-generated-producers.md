# Generated cursor producer conformance

These cases specify [generated-producers/1](../language/generated-producers.md).
Fixtures supply exact schemas, nominal producer/cursor definitions, generic
mapping, capture/access facts and bounded producer operation graphs.
They do not implement a source compiler, arbitrary frame/lifetime/effect
proofs, GC, task representation or physical producer codecs.

## Registration and nominal roles

| ID | Scenario | Required outcome |
|---|---|---|
| PGREG-01 | Exact IteratorCursor schema marks a primary mut unique struct | Recognize its owned named cursor shell |
| PGREG-02 | Exact Iterator marks the shell's same-module completed producer block | Associate one callable definition with that shell |
| PGREG-03 | Same-spelled user attributes or member names are supplied | No compiler generation from spelling/shape |
| PGREG-04 | Either schema receives arguments or repeats on effective target | Reject exact schema/multiplicity mismatch |
| PGREG-05 | IteratorCursor targets callable/result/parameter/class/non-unique struct | Reject target/form mismatch |
| PGREG-06 | Iterator targets type/result/parameter, bodyless callable or expression body | Reject producer declaration |
| PGREG-07 | Secondary partial or foreign module tries to add/change generation | Reject primary/owner violation |
| PGREG-08 | Owned shell lacks its producer, or producer lacks its marked shell | Reject incomplete pair |
| PGREG-09 | Two callable definitions claim one shell | Diagnose even with equal bodies/signatures or overload name |
| PGREG-10 | Repeated import paths identify one generation record | Deduplicate exact identity |
| PGREG-11 | Each of the six exact direct roles is selected with matched source timing | Bind its exact value/readonly/writable output and advance/close |
| PGREG-12 | Source async modifier disagrees with selected role | Reject; no timing fallback |
| PGREG-13 | Declared producer result is sequence factory or Task<Cursor> | Reject standard generated result; explicit wrapper needed |
| PGREG-14 | Shell declares multiple cursor roles/additional conformances | Reject initial shell; use explicit adapter |
| PGREG-15 | Shell promises Copy or has authored fields/constructor/layout controls | Reject compiler-owned affine frame conflict |
| PGREG-16 | Shell omits required cursor members | Generate selected exact members with inherited contracts |
| PGREG-17 | Shell writes exact required bodyless contract declarations | Supply generated implementations, not an ordinary bodyless-method exemption |
| PGREG-18 | Written generated member changes init-output type/access or adds overload/body | Reject exact slot mismatch |
| PGREG-19 | Async generated slot has authored completion clauses | Its async declaration binds completed payload and inherited reservation |
| PGREG-20 | Ordinary unmarked concrete method is bodyless | Generation does not make it legal |
| PGREG-21 | Caller attempts default/type-call/new construction of shell | Reject; only associated producer creates initial live frame |
| PGREG-22 | Equal producer bodies/layouts belong to distinct declared shells | Preserve distinct nominal identities |
| PGREG-23 | Private/foreign frame name is used as result inference evidence | No body-driven result/item type inference |
| PGREG-24 | Compiler lowering moves authored body into generated cursor | Preserve original code owner/source unit; no new private access grant |

## Generic pair checking

| ID | Scenario | Required outcome |
|---|---|---|
| PGGEN-01 | Producer formals map one-to-one into shell arguments | Check definitions and substitute forward |
| PGGEN-02 | Two type/constant formals are reordered | Valid when exact kinds/domains/constraints map |
| PGGEN-03 | Result omits an own or enclosing formal | Reject hidden representation/capability choice |
| PGGEN-04 | Result repeats one formal into two shell positions | Reject non-bijective mapping |
| PGGEN-05 | Result wraps a formal inside another constructed type | Not the initial direct-formal mapping |
| PGGEN-06 | Nongeneric producer claims only one fixed instantiation of generic shell | Reject incomplete shell definition association |
| PGGEN-07 | Formal kinds or nominal constant domains differ | Reject despite equal carrier representation |
| PGGEN-08 | Mapped constraints are not equivalent | Reject hidden body requirements or unsupported shell instantiations |
| PGGEN-09 | Equivalent mapped constraint evidence is supplied | Check body under that shared definition contract |
| PGGEN-10 | Convenient concrete specialization makes invalid generic body work | Cannot repair definition |
| PGGEN-11 | Complete type qualification changes under mapping | Preserve exact item/capture types; no attenuation to repair |
| PGGEN-12 | Ordinary call/type use selects a declared default argument | Expand normally after definition binding; no frame inference |
| PGGEN-13 | Transparent alias denotes the same complete shell | Normalize identity; alias cannot own a second generation record |
| PGGEN-14 | Captured generated field storage is unsupported for valid instantiation | Diagnose capability/representation before affected use |
| PGGEN-15 | Private generated body/layout changes with same cursor nominal name | Apply retained complete layout/ABI and replacement contract |

## Capture and creation

| ID | Scenario | Required outcome |
|---|---|---|
| PGCAP-01 | Source arguments/defaults/params construction have effects | Evaluate once eagerly before capture/creation; body remains lazy |
| PGCAP-02 | Creator captures Copy values | Ordinary input copies; one affine traversal, no copied cursor |
| PGCAP-03 | Creator receives named affine input without move | Reject missing transfer |
| PGCAP-04 | Affine input is explicitly moved into capture | Caller source stays vacant; frame owns it until transfer/cleanup |
| PGCAP-05 | Creator captures receiver or ref/write ref input | Preserve actual storage/access/backing and declared extent |
| PGCAP-06 | Caller temporary or guard cannot survive delayed use/close | Reject capture; no hidden arena/lifetime extension |
| PGCAP-07 | Input is unused by current body but remains listed in capture bounds | Retain bound; no private-body shortening |
| PGCAP-08 | Creator declares init ref formal for later body initialization | Reject deferred invocation output |
| PGCAP-09 | Creator's first or later require fails | Synchronous failure; clean captures/phase locals once; no body cleanup |
| PGCAP-10 | Creator ensure fails after complete private cursor formation | Prevent publication; clean unpublished captures without running body |
| PGCAP-11 | Creator ensures use contextual return | Observe complete cursor, not item or final body unit |
| PGCAP-12 | Creation succeeds but cursor is never advanced | No body effects; captures and close responsibility already exist |
| PGCAP-13 | Async producer creation fails | Still synchronous; later async body does not alter creation channel |
| PGCAP-14 | Transferred capture requires pending async cleanup on creation failure | Require admitted synchronous failure release/transfer or failure-free suffix; no stranded owner |
| PGCAP-15 | Creation contracts introduce phase-local bindings | Isolated phase scopes, not captured producer body locals |
| PGCAP-16 | Cursor is acquired/adopted by a loop | Adoption does not run body or duplicate captures |
| PGCAP-17 | Reusable sequence wrapper creates cursors from Copy/borrowable inputs | Explicit checked recreation/retention, not automatic producer factory |
| PGCAP-18 | Readonly sequence factory would move an affine capture | Reject implicit consuming acquisition |
| PGCAP-19 | Captured state is affine/nontransferable or requires a root/lease | Preserve actual requirements through suspension and close |
| PGCAP-20 | Generation marker is used to infer thread sharing, pinning or allocation | No such capability from marker |
| PGCAP-21 | Static producer tries to capture enclosing caller locals implicitly | No caller-local recipe capture; ordinary inputs only |
| PGCAP-22 | Declaration signature has imported capture/load dependencies | Retain exact independent definition/access/dependency identities |

## Yield and body state

| ID | Scenario | Required outcome |
|---|---|---|
| PGYIELD-01 | First advance reaches first yield | Start body once; evaluate operand once; publish before caller body |
| PGYIELD-02 | Later advance reaches another yield | Resume same activation after previous yield; no creator replay |
| PGYIELD-03 | Scope local/defer/handler was entered before yield | Retain its actual state and cleanup registration |
| PGYIELD-04 | Resumption remains in same block | Do not reinitialize its locals/materialization sites |
| PGYIELD-05 | Producer loop later reenters a block | Ordinary new supporting-scope activation |
| PGYIELD-06 | Value yield has exact contextual T | Ordinary value binding, no universal Copy requirement |
| PGYIELD-07 | Non-Copy owner local is explicitly moved into output | Transfer once; source vacant; caller owns published value |
| PGYIELD-08 | Value yield tries to extract non-Copy stored element from borrowed source | Reject absent extraction authority |
| PGYIELD-09 | Value T is void or an absent optional | Valid live item, not exhaustion |
| PGYIELD-10 | Value T is itself a reference descriptor | Ordinary value item with its actual dependencies, not lending alias |
| PGYIELD-11 | Readonly/writable lending yield forms ref place | Preserve exact stored T and actual immediate authority |
| PGYIELD-12 | Lent non-Copy local is live in suspended frame | Valid cursor-bound loan without copying/owning T |
| PGYIELD-13 | Lending output points to dead/prohibited place or void payload | Reject invalid descriptor/origin |
| PGYIELD-14 | Output check faults after forming owned value or loan | No publication; clean owning value/loan once under actual contract |
| PGYIELD-15 | Suspended producer's cursor-bound loan is still live | Reject invalidating next advance/close |
| PGYIELD-16 | Available published refinement proves source preservation | Preserve remaining source/guard dependencies; no body-derived narrowing |
| PGYIELD-17 | Yield appears in finally/defer cleanup | Reject item publication during cleanup/close |
| PGYIELD-18 | Nested ordinary lambda/function contains yield | It does not inherit producer eligibility |
| PGYIELD-19 | Yield remains inside entered try/catch scope | Retain valid handler/failure state and close cleanup |
| PGYIELD-20 | Async producer awaits between yields | Retain actual state, affinity and operation responsibility |
| PGYIELD-21 | Synchronous producer retains unperformed async cleanup at yield | Reject unsupported close state |
| PGYIELD-22 | Producer uses return expression, yield return or yield break | Reject forms; return unit/fallthrough finishes production |
| PGYIELD-23 | Closing suspended cursor would require entering a later body scope | Never enter it or evaluate its remaining operands |
| PGYIELD-24 | Private body checks or catch recovery precede a successful yield | Keep ordinary effects/failure handling and checked output relation |
| PGYIELD-25 | Generated local storage/materialization is used to extend a loan | No per-item arena or backing repair |

## Contract phases and effects

| ID | Scenario | Required outcome |
|---|---|---|
| PGPHASE-01 | Creator has require/ensure and multiple advances | Creator clauses execute once at synchronous creation |
| PGPHASE-02 | Generated advance has an additional true-output ensure | Check at each item publication and prove for every instance |
| PGPHASE-03 | Generated advance ensure reads item on false without a guard | Reject vacant-output read |
| PGPHASE-04 | Generated async slot ensure uses return | Completed bool/unit, not task handle or creator cursor |
| PGPHASE-05 | Generated slot requires narrow inherited valid caller domain | Reject incompatible witness contract |
| PGPHASE-06 | Body reaches return/fallthrough | Body cleanup, then outer-body completion ensures, then advance false/vacant clauses; failure skips later normal checks without replay |
| PGPHASE-07 | Outer-body ensure was registered before yields | Run only on successful body completion, not each yield/close |
| PGPHASE-08 | Exhausted cursor is advanced repeatedly | No body/completion replay; applicable operation clauses still run |
| PGPHASE-09 | Advance faults or early close occurs | No normal body completion guarantee |
| PGPHASE-10 | Cursor check observes moved input/released body guard | Reject unavailable source; no retained-byte or snapshot repair |
| PGPHASE-11 | Creator is Pure but resumed body writes externally | Creation promise alone does not authorize pure advancement |
| PGPHASE-12 | Creator NoThrow succeeds but resumed body/close can fail | Later operation needs its own promise; no implicit propagation |
| PGPHASE-13 | Creator parameter has NoAlias | No post-creation separation extent for captured accesses |
| PGPHASE-14 | Creator has ConstEval | No automatic constant advancement/close or encodable affine cursor result |
| PGPHASE-15 | Generated operation declares effect/evaluator capability | Check resumed code, captures, output, clauses and cleanup under admitted support |
| PGPHASE-16 | Additional slot ensure contradicts inherited output state | Reject contract, not fabricated true/default/false output |
| PGPHASE-17 | Source creator ensure is interpreted as per-yield/final check | Wrong phase; no inferred selector or item/result binding |
| PGPHASE-18 | Failure replaces pending yield/completion after actual effects | No rollback; preserve exceptional output cleanup and cause |

## Close and asynchronous responsibility

| ID | Scenario | Required outcome |
|---|---|---|
| PGCLOSE-01 | Never-advanced cursor closes | Clean captures once without entering body |
| PGCLOSE-02 | Cursor closes after one yield in nested scopes | Unwind entered scopes in ordinary order, not remaining code |
| PGCLOSE-03 | Cursor exhausted normally before close | Do not replay completed body/capture cleanup |
| PGCLOSE-04 | Advance failed after required unwind | Close cleanup-safe state without retrying production |
| PGCLOSE-05 | Close cleanup faults after body failure | Preserve causal chain and once-only residual cleanup; no second close |
| PGCLOSE-06 | Manual owner retires/discards cursor without close | Undischarged traversal responsibility, not destructor substitution |
| PGCLOSE-07 | Cursor ownership is moved | Transfer existing close obligation, not a new traversal |
| PGCLOSE-08 | Loop exits by continue/break/return/failure | Retire body item/loans, then next advance or required close in order |
| PGCLOSE-09 | Async advance is physically terminal but unobserved | Reservation still blocks caller use/close |
| PGCLOSE-10 | Async cursor closes after pending advance settles | Observe outcome, settle published item/loans, then await close |
| PGCLOSE-11 | Old task is reobserved after descriptor destination reuse | No new-version proof or release of new pending reservation |
| PGCLOSE-12 | CloseAsync suspends during entered cleanup | Retain cursor/backing/root/guard/owner until checked completion |
| PGCLOSE-13 | Header/source cleanup would precede async close observation | Reject schedule |
| PGCLOSE-14 | Cancellation request/abandoned wait occurs | No automatic completion, close or backing release |
| PGCLOSE-15 | Lent cursor-owned scratch is retired | First end every loan; holder may then clean T once |
| PGCLOSE-16 | Lending descriptor ends over a source-owned element | Release loan only; do not destroy source element |
| PGCLOSE-17 | Early close is treated as successful final completion | No body ensure or normal exhaustion guarantee |
| PGCLOSE-18 | Destructor would run throwing/suspending authored cleanup | Reject substitution; destructor is nonthrowing/nonsuspending residual retirement |
| PGCLOSE-19 | Cursor retirement invalidates a surviving frame-bound reference | Reject incompatible dependency |
| PGCLOSE-20 | Close attempts to emit another yielded item or rerun a saved operand | No such publication/replay |

## Independent contracts and representation

| ID | Scenario | Required outcome |
|---|---|---|
| PGRET-01 | Retained producer record identifies exact primary type/callable/role | Preserve identities, complete arguments and source code owner |
| PGRET-02 | Replacement changes capture, creation/advance phase or output origin | Incompatible relied-on contract |
| PGRET-03 | Replacement changes affine ownership, close or failure schedule | Incompatible contract |
| PGRET-04 | Linker remaps type/formal/member/source/phase references | Preserve semantic mappings, not rows/debug names |
| PGRET-05 | Consumer lacks required producer/capture/frame capability | Reject before affected use |
| PGRET-06 | Private implementation alone claims stronger lifetime/effect facts | No public contract strengthening without checked publication |
| PGRET-07 | Generated frame is assumed layout-independent across body edits | Retain actual layout/ABI dependency or explicit independently admitted opaque contract |
| PGRET-08 | Held opcode/table slot or optional Attr field is used for required generation | No physical allocation/codec selected |
| PGRET-09 | Module/source ownership is inferred from generated debug names | Require retained semantic owner/unit identities |
| PGRET-10 | Arbitrary generated-body assertion is trusted solely because record is present | Explicit producer/audit trust or validation, not flag proof |
| PGRET-11 | Named generated type is treated as a freestanding runtime requirement | Required task/reference/root/capture capabilities remain profile-explicit |
| PGRET-12 | Finite producer traces and schema fixtures pass | Not compiler, arbitrary body/lifetime, GC, concurrency or codec conformance |
