using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SolarStudios
{
    public class PlayerUnitMoveState : MonoBehaviour, IUnitState
    {
        private static readonly List<PlayerUnitMoveState> All = new List<PlayerUnitMoveState>();
        private const int MaxRouteChecks = 4;
        private static int lastOrderGroup;

        public static int NextOrderGroup()
        {
            return ++lastOrderGroup;
        }

        private PlayerUnitStateMachine stateMachine;
        private NavMeshAgent agent;

        public List<Vector3> destinations = new List<Vector3>();
        public float tiltMultiplier;
        public int maxTilt;
        public GameObject gfx;

        [Header("Arrival")]
        public float arrivalTolerance = 4f;
        public float stuckTime = 2f;
        public float minProgress = 1f;
        public float stuckRadiusMultiplier = 3f;

        [Header("Routing Around Parked Ships")]
        public float routeMargin = 6f;
        public float stopShortRadii = 5f;
        public float lineStandoff = 6f;
        [Range(-1f, 1f)] public float sameDirectionThreshold = 0.7f;

        [Header("Priority Move - Making Way")]
        public float yieldCheckInterval = 0.25f;
        public float yieldLookAheadRadii = 3f;
        public float yieldMargin = 4f;

        private float currentTilt = 0f;
        private Quaternion lastRotation;

        private PlayerUnitStateMachine machine;
        private NavMeshAgent ownAgent;
        private UnitHealthManager health;

        private bool hasActiveDestination;
        private Vector3 activeDestination;
        private int issuedFrame;
        private float bestRemaining;
        private float progressTimer;
        private float nextYieldCheck;

        private bool priorityMove;
        private int detourPoints;
        private int routeChecks;
        private int orderGroup;
        private Transform ignoredObstacle;

        private bool hasFacing;
        private Vector3 facing;

        public Vector3 LastQueuedPosition => destinations.Count > 0 ? destinations[destinations.Count - 1] : transform.position;
        public bool IsPriorityMove => priorityMove;

        private bool IsParked => machine == null || (Object)machine.currentState != this || destinations.Count == 0;
        private bool IsDead => health != null && health.IsDead;
        private Vector3 Position => ownAgent.transform.position;

        private Vector3 TravelDirection
        {
            get
            {
                if (IsParked) return Vector3.zero;
                Vector3 direction = Flat(destinations[0] - Position);
                return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            }
        }

        private void Awake()
        {
            machine = GetComponentInParent<PlayerUnitStateMachine>();
            ownAgent = GetComponentInParent<NavMeshAgent>();
            health = GetComponentInParent<UnitHealthManager>();
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        public void Enter(PlayerUnitStateMachine stateMachine) // Runs when we enter the state
        {
            this.stateMachine = stateMachine;
            agent = GetComponentInParent<NavMeshAgent>();
            lastRotation = agent.transform.rotation;
            hasActiveDestination = false;

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        public void Run() // Runs every frame
        {
            if (!agent.isOnNavMesh)
            {
                destinations.Clear();
            }

            if (destinations.Count == 0)
            {
                if (!TurnToFacing())
                {
                    stateMachine.SetState(agent.GetComponentInChildren<PlayerUnitIdleState>());
                    return;
                }
            }
            else
            {
                FollowDestinations();
            }

            Tilt();

            lastRotation = agent.transform.rotation;
        }

        public void Exit() // Runs when we exit the state
        {
            hasActiveDestination = false;
        }

        private void FollowDestinations()
        {
            if (!hasActiveDestination || activeDestination != destinations[0])
            {
                if (!priorityMove && detourPoints == 0 && routeChecks < MaxRouteChecks)
                {
                    PlanAroundParkedShips();
                    if (destinations.Count == 0) return;
                }

                IssueDestination(destinations[0]);
            }

            if (priorityMove && Time.time >= nextYieldCheck)
            {
                nextYieldCheck = Time.time + yieldCheckInterval;
                ClearPathAhead();
            }

            if (!ReachedActiveDestination()) return;

            destinations.RemoveAt(0);
            hasActiveDestination = false;

            if (detourPoints > 0)
            {
                detourPoints--;
            }
            else
            {
                routeChecks = 0;
            }

            if (destinations.Count == 0)
            {
                agent.ResetPath();
            }
        }

        private void IssueDestination(Vector3 destination)
        {
            activeDestination = destination;
            hasActiveDestination = true;
            issuedFrame = Time.frameCount;
            bestRemaining = float.MaxValue;
            progressTimer = 0f;
            agent.SetDestination(destination);
        }

        private bool ReachedActiveDestination()
        {
            if (agent.pathPending || Time.frameCount == issuedFrame) return false;
            if (!agent.hasPath) return true;

            float remaining = agent.remainingDistance;
            if (float.IsInfinity(remaining) || float.IsNaN(remaining))
            {
                remaining = Vector3.Distance(agent.transform.position, activeDestination);
            }

            if (remaining <= Mathf.Max(agent.stoppingDistance, arrivalTolerance)) return true;

            if (remaining < bestRemaining - minProgress)
            {
                bestRemaining = remaining;
                progressTimer = 0f;
                return false;
            }

            progressTimer += Time.deltaTime;
            if (progressTimer < stuckTime) return false;

            if (remaining < agent.radius * stuckRadiusMultiplier) return true;

            if (priorityMove)
            {
                IssueDestination(activeDestination);
                ClearPathAhead();
            }
            else
            {
                hasActiveDestination = false;
            }
            return false;
        }

        private void PlanAroundParkedShips()
        {
            routeChecks++;

            Vector3 start = Position;
            Vector3 goal = destinations[0];
            Vector3 toGoal = Flat(goal - start);
            float length = toGoal.magnitude;
            if (length < arrivalTolerance) return;

            Vector3 heading = toGoal / length;
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float selfRadius = ownAgent.radius;

            List<PlayerUnitMoveState> obstacles = new List<PlayerUnitMoveState>();
            foreach (PlayerUnitMoveState other in All)
            {
                if (other == this || other.ownAgent == null || !other.ownAgent.enabled || other.IsDead) continue;
                if (ignoredObstacle != null && other.ownAgent.transform.IsChildOf(ignoredObstacle)) continue;
                if (!other.IsParked)
                {
                    if (orderGroup != 0 && other.orderGroup == orderGroup) continue;
                    if (Vector3.Dot(other.TravelDirection, heading) > sameDirectionThreshold) continue;
                }
                if (Flat(other.Position - start).magnitude < BlockRadius(other, selfRadius)) continue;
                obstacles.Add(other);
            }

            PlayerUnitMoveState first = null;
            float firstEntry = float.MaxValue;
            foreach (PlayerUnitMoveState other in obstacles)
            {
                Vector3 offset = Flat(other.Position - start);
                float along = Vector3.Dot(offset, heading);
                float lateral = Vector3.Dot(offset, right);
                float radius = BlockRadius(other, selfRadius);
                if (Mathf.Abs(lateral) >= radius || along <= 0f || along - radius > length) continue;

                float entry = along - Mathf.Sqrt(radius * radius - lateral * lateral);
                if (entry < firstEntry)
                {
                    firstEntry = entry;
                    first = other;
                }
            }

            if (first == null) return;

            List<PlayerUnitMoveState> line = GatherLine(first, obstacles, selfRadius);

            float lineStart = float.MaxValue;
            float lineEnd = float.MinValue;
            float leftEdge = float.MaxValue;
            float rightEdge = float.MinValue;
            bool goalInsideLine = false;

            foreach (PlayerUnitMoveState member in line)
            {
                Vector3 offset = Flat(member.Position - start);
                float along = Vector3.Dot(offset, heading);
                float lateral = Vector3.Dot(offset, right);
                float radius = BlockRadius(member, selfRadius);

                lineStart = Mathf.Min(lineStart, along - radius);
                lineEnd = Mathf.Max(lineEnd, along + radius);
                leftEdge = Mathf.Min(leftEdge, lateral - radius);
                rightEdge = Mathf.Max(rightEdge, lateral + radius);

                if (Flat(member.Position - goal).magnitude < radius)
                {
                    goalInsideLine = true;
                }
            }

            if (goalInsideLine || length - lineEnd < stopShortRadii * selfRadius)
            {
                float stopAlong = firstEntry - lineStandoff;
                destinations[0] = stopAlong <= arrivalTolerance ? start : Snap(start + heading * stopAlong, selfRadius);
                return;
            }

            float rightOffset = rightEdge + routeMargin;
            float leftOffset = leftEdge - routeMargin;
            float beforeAlong = Mathf.Max(0f, lineStart - routeMargin);
            float afterAlong = Mathf.Min(length, lineEnd + routeMargin);

            bool preferRight = Mathf.Abs(rightOffset) <= Mathf.Abs(leftOffset);
            if (!TryDetour(start, heading, right, preferRight ? rightOffset : leftOffset, beforeAlong, afterAlong, selfRadius)
                && !TryDetour(start, heading, right, preferRight ? leftOffset : rightOffset, beforeAlong, afterAlong, selfRadius))
            {
                return;
            }
        }

        private bool TryDetour(Vector3 start, Vector3 heading, Vector3 right, float lateral, float beforeAlong, float afterAlong, float selfRadius)
        {
            Vector3 entry = start + heading * beforeAlong + right * lateral;
            Vector3 exit = start + heading * afterAlong + right * lateral;

            if (!NavMesh.SamplePosition(entry, out NavMeshHit entryHit, selfRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(exit, out NavMeshHit exitHit, selfRadius, NavMesh.AllAreas)) return false;

            destinations.Insert(0, exitHit.position);
            destinations.Insert(0, entryHit.position);
            detourPoints = 2;
            return true;
        }

        private List<PlayerUnitMoveState> GatherLine(PlayerUnitMoveState seed, List<PlayerUnitMoveState> candidates, float selfRadius)
        {
            List<PlayerUnitMoveState> line = new List<PlayerUnitMoveState> { seed };
            List<PlayerUnitMoveState> open = new List<PlayerUnitMoveState>(candidates);
            open.Remove(seed);

            for (int i = 0; i < line.Count; i++)
            {
                PlayerUnitMoveState current = line[i];
                for (int j = open.Count - 1; j >= 0; j--)
                {
                    PlayerUnitMoveState other = open[j];
                    float gap = Flat(other.Position - current.Position).magnitude - current.ownAgent.radius - other.ownAgent.radius;
                    if (gap < selfRadius * 2f + routeMargin)
                    {
                        line.Add(other);
                        open.RemoveAt(j);
                    }
                }
            }

            return line;
        }

        private float BlockRadius(PlayerUnitMoveState other, float selfRadius)
        {
            return other.ownAgent.radius + selfRadius + yieldMargin;
        }

        private void ClearPathAhead()
        {
            Vector3 position = agent.transform.position;
            Vector3 heading = Flat(agent.desiredVelocity);
            if (heading.sqrMagnitude < 0.01f)
            {
                heading = Flat(agent.steeringTarget - position);
            }
            if (heading.sqrMagnitude < 0.01f) return;
            heading.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float lookAhead = agent.radius * yieldLookAheadRadii;
            float remaining = Vector3.Distance(position, activeDestination);
            int layer = agent.gameObject.layer;

            foreach (PlayerUnitMoveState other in All)
            {
                if (other == this || other.ownAgent == null || !other.IsParked || other.IsDead) continue;
                if (other.ownAgent.gameObject.layer != layer || !other.ownAgent.isOnNavMesh) continue;

                Vector3 offset = Flat(other.ownAgent.transform.position - position);
                float along = Vector3.Dot(offset, heading);
                float clearance = agent.radius + other.ownAgent.radius + yieldMargin;
                if (along <= 0f || along > Mathf.Min(lookAhead, remaining) + clearance) continue;

                float lateral = Vector3.Dot(offset, right);
                if (Mathf.Abs(lateral) >= clearance) continue;

                float side = Mathf.Abs(lateral) > 0.5f ? Mathf.Sign(lateral) : (other.GetInstanceID() > GetInstanceID() ? 1f : -1f);
                other.StepAside(right * side * (clearance - Mathf.Abs(lateral)));
            }
        }

        private void StepAside(Vector3 shift)
        {
            if (machine == null) return;

            Vector3 target = ownAgent.transform.position + shift;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, shift.magnitude + ownAgent.radius, NavMesh.AllAreas))
            {
                target = hit.position;
            }

            Vector3 heading = hasFacing ? facing : Flat(ownAgent.transform.forward);

            ClearDestinations();
            destinations.Add(target);
            SetFacing(heading);
            priorityMove = true;
            machine.SetState(this);
        }

        private bool TurnToFacing()
        {
            if (!hasFacing) return false;

            Vector3 forward = Flat(agent.transform.forward);
            float angle = Vector3.SignedAngle(forward, facing, Vector3.up);

            if (Mathf.Abs(angle) < 1f)
            {
                hasFacing = false;
                return false;
            }

            float turn = Mathf.Min(Mathf.Abs(angle), agent.angularSpeed * Time.deltaTime);
            agent.transform.Rotate(0f, Mathf.Sign(angle) * turn, 0f, Space.World);
            return true;
        }

        public void Tilt()
        {
            float turnAmount = Vector3.SignedAngle(lastRotation * Vector3.forward, agent.transform.forward, Vector3.up);


            if (Mathf.Abs(turnAmount) > 0.5f)
            {
                float targetTilt = Mathf.Clamp(Mathf.Abs(turnAmount) * tiltMultiplier, -maxTilt, maxTilt);

                currentTilt = Mathf.Lerp(currentTilt, targetTilt * -Mathf.Sign(turnAmount), Time.deltaTime * 5f);
            }
            else
            {
                currentTilt = Mathf.Lerp(currentTilt, 0, Time.deltaTime * 5f);
            }

            gfx.transform.localRotation = Quaternion.Euler(0, 0, currentTilt);
        }

        public void AddDestination(Vector3 destination)
        {
            destinations.Add(destination);
        }

        public void ClearDestinations()
        {
            if (ownAgent != null && ownAgent.isOnNavMesh && ownAgent.hasPath)
            {
                ownAgent.ResetPath();
            }

            destinations.Clear();
            hasActiveDestination = false;
            hasFacing = false;
            priorityMove = false;
            detourPoints = 0;
            routeChecks = 0;
            orderGroup = 0;
            ignoredObstacle = null;
        }

        public void SetPriority(bool priority)
        {
            priorityMove = priority;
        }

        public void SetOrderGroup(int group)
        {
            orderGroup = group;
        }

        public void IgnoreObstacle(Transform obstacle)
        {
            ignoredObstacle = obstacle;
        }

        public void SetFacing(Vector3 direction)
        {
            direction.y = 0f;
            hasFacing = direction.sqrMagnitude > 0.0001f;
            facing = hasFacing ? direction.normalized : Vector3.zero;
        }

        private static Vector3 Snap(Vector3 point, float searchRadius)
        {
            return NavMesh.SamplePosition(point, out NavMeshHit hit, searchRadius * 2f, NavMesh.AllAreas) ? hit.position : point;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }
    }
}
