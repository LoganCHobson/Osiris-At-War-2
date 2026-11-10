// Minimal signatures of UnityEngine and Osiris types, only to type-check the Grand Admiral changes.
using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name; public static T Instantiate<T>(T o) where T : Object => o; public static void Destroy(Object o) { } public static void DontDestroyOnLoad(Object o) { } public static implicit operator bool(Object o) => o != null; }
    public class Component : Object { public Transform transform; public GameObject gameObject; public T GetComponent<T>() => default; public T[] GetComponents<T>() => new T[0]; public bool TryGetComponent<T>(out T c) { c = default; return false; } }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) => null; }
    public class Coroutine { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    public class GameObject : Object { public GameObject(string n) { } public T AddComponent<T>() where T : Component, new() => new T(); public T GetComponent<T>() => default; }
    public class Transform : Component { public Vector3 position; public Vector3 forward; public Quaternion rotation; }
    public class TextAsset : Object { public byte[] bytes; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero, up, forward, positiveInfinity; public Vector3 normalized => this; public float sqrMagnitude => 0; public float magnitude => 0;
        public static float Distance(Vector3 a, Vector3 b) => 0; public static float Dot(Vector3 a, Vector3 b) => 0; public static Vector3 Cross(Vector3 a, Vector3 b) => a; public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a; public static Vector3 operator -(Vector3 a, Vector3 b) => a; public static Vector3 operator -(Vector3 a) => a;
        public static Vector3 operator *(Vector3 a, float b) => a; public static Vector3 operator *(float b, Vector3 a) => a; public static Vector3 operator /(Vector3 a, float b) => a; }
    public struct Vector2 { public Vector2(float x, float y) { } }
    public struct Quaternion { public static Quaternion LookRotation(Vector3 f) => default; public static Quaternion identity; public static Vector3 operator *(Quaternion q, Vector3 v) => v; }
    public static class Mathf { public static float Max(float a, float b) => a; public static float Min(float a, float b) => a; public static float Lerp(float a, float b, float t) => a; public static float Clamp01(float v) => v; public static int Clamp(int v, int a, int b) => v; public static float Clamp(float v, float a, float b) => v; }
    public static class Random { public static float value; public static int Range(int a, int b) => a; }
    public static class Time { public static float time, deltaTime, realtimeSinceStartup; }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } }
    public static class Application { public static string persistentDataPath = "", version = "0"; }
    public static class Resources { public static T Load<T>(string p) where T : Object => null; }
    public static class PlayerPrefs { public static int GetInt(string k, int d) => d; public static void SetInt(string k, int v) { } public static void Save() { } }
    public static class JsonUtility { public static string ToJson(object o) => ""; public static T FromJson<T>(string s) => default; }
    public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float s) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
}
namespace UnityEngine.AI
{
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh { public const int AllAreas = -1; public static bool SamplePosition(Vector3 p, out NavMeshHit h, float d, int m) { h = default; return false; } }
    public class NavMeshAgent : Component { public bool isOnNavMesh, hasPath, isStopped; public bool SetDestination(Vector3 v) => true; }
}
namespace UnityEngine.Networking
{
    public class UploadHandlerRaw : IDisposable { public UploadHandlerRaw(byte[] b) { } public string contentType; public void Dispose() { } }
    public class DownloadHandlerBuffer : IDisposable { public byte[] data; public string text; public void Dispose() { } }
    public class UnityWebRequestAsyncOperation { }
    public class UnityWebRequest : IDisposable {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public const string kHttpVerbPOST = "POST";
        public UnityWebRequest(string url, string method) { }
        public static UnityWebRequest Get(string url) => null;
        public UploadHandlerRaw uploadHandler; public DownloadHandlerBuffer downloadHandler; public int timeout; public Result result; public long responseCode;
        public void SetRequestHeader(string k, string v) { } public UnityWebRequestAsyncOperation SendWebRequest() => null; public void Dispose() { } }
}
namespace SolarStudios
{
    public interface IUnitState { }
    public class PlayerUnitMoveState : UnityEngine.MonoBehaviour, IUnitState { public static int NextOrderGroup() => 0; public void ClearDestinations() { } public void AddDestination(UnityEngine.Vector3 v) { } public void SetFacing(UnityEngine.Vector3 v) { } public void SetOrderGroup(int g) { } }
    public class PlayerUnitStateMachine : UnityEngine.MonoBehaviour { public IUnitState currentState; public void SetState(IUnitState s) { } }
}
public enum FleetRole { Line, Screen, Carrier, Fighter, Interceptor, Bomber }
public enum ShipType { Station, Battleship, Carrier, Cruiser, Destroyer, Corvette, Fighter }
public class Ship : UnityEngine.MonoBehaviour { public float combatPower; public int populationCost; public FleetRole fleetRole; public bool CanLead => true; }
public class Faction : UnityEngine.ScriptableObject { public AIPersonality personality; public bool isNeutral, isPlayerFaction; public string factionName; }
public class UnitHealthManager : UnityEngine.MonoBehaviour { public static readonly List<UnitHealthManager> Active = new List<UnitHealthManager>(); public float maxHealth, currentHealth; public Ship sourceShip; public bool isAttackerSide, isShipyardBonus, isBattleStation; public bool IsDead => false; public bool IsDefense => false; }
public class SpaceUnit : UnityEngine.MonoBehaviour { public UnityEngine.AI.NavMeshAgent agent; public SolarStudios.PlayerUnitMoveState moveState; public SolarStudios.PlayerUnitStateMachine stateMachine; public float maxRange; public ShipType shipType; }
public class HardpointHealth : UnityEngine.MonoBehaviour { public float currentHealth; }
public class HardpointManager : UnityEngine.MonoBehaviour { public List<HardpointHealth> hardpoints; public void AssignTarget(UnityEngine.Transform t) { } public List<T> GetSpecificHardpoints<T>() => new List<T>(); }
public class TurretController : UnityEngine.MonoBehaviour { public enum TargetFilter { All, FightersOnly } public TargetFilter targetFilter; public float damage, fireRate; public List<UnityEngine.Transform> firingPoints; }
public class EngineHardpoint : UnityEngine.MonoBehaviour { }
public class Squadron : UnityEngine.MonoBehaviour { public enum Role { Fighter, Interceptor, Bomber } public Role CraftRole { get; private set; } public float Dps() => 0; }
public static class FleetFormation { public static float Spacing(List<SpaceUnit> u) => 0; }
public static class FTLJump { public static void Arrive(UnityEngine.GameObject g) { } }
public static class BattleDeployment { public class Placement { public ShipEntry ship; public UnityEngine.Vector3 position; public float altitude; } public static List<Placement> Arrange(IList<ShipEntry> s, ShipEntry l, UnityEngine.Vector3 a, UnityEngine.Vector3 f) => null; public static List<Placement> ArrangeFormation(IList<ShipEntry> s, DefenseFormation d, ShipEntry l, UnityEngine.Vector3 a, UnityEngine.Vector3 f) => null; public static ShipEntry ChooseLead(IEnumerable<ShipEntry> s) => null; }
public class BattleTerrain { public static BattleTerrain Instance; public bool CanDrop(UnityEngine.Vector3 p, bool a) => true; public UnityEngine.Vector3 NearestDropPoint(UnityEngine.Vector3 p, bool a) => p; public bool InField(UnityEngine.Vector3 p) => false; public bool CrossesField(UnityEngine.Vector3 a, UnityEngine.Vector3 b) => false; public static float SizeFactor(ShipType t) => 1; public static void SetAIPriority(SpaceUnit u, bool p) { } }
public class RetreatManager { public static RetreatManager Instance; public bool IsRetreating(bool a) => false; public bool CanRetreat(bool a) => false; public void BeginRetreat(bool a) { } public bool IsBlocked(bool a) => false; }
public class GameManager { public static GameManager Instance; public UnityEngine.GameObject SpawnShip(Ship s, UnityEngine.Vector3 p, UnityEngine.Quaternion r, bool a) => null; }
public class BattleContext { public static BattleContext Instance; public bool hasPendingBattle, defenderHasShipyard, defenderHasBattleStation; public List<ShipEntry> attackerRoster, defenderRoster; public Faction attackerFaction, defenderFaction; }
public static class Strength { public const float ShipyardPower = 150f, BattleStationPower = 500f, DefaultShipPower = 100f; public static float Of(List<Ship> r) => 0; public static float Of(List<ShipEntry> r) => 0; public static bool IsPlayer(Faction f) => false; }
public class FactionBrain { public AIPersonality personality; }
public class AIDirector { public static AIDirector Instance; public FactionBrain BrainFor(Faction f) => null; }
public static class GameSettings { public static bool ShareBattleData = true; }

public class ShipEntry { public Ship type; public string shipName, hero; public static implicit operator Ship(ShipEntry e) => e?.type; public bool IsHero => false;
    public float combatPower => 0; public int populationCost => 0; public FleetRole fleetRole => FleetRole.Line; public bool CanLead => true; public UnityEngine.GameObject prefab => null; }
public class ShipAltitude : UnityEngine.MonoBehaviour { public static float AltitudeOf(UnityEngine.Transform t) => 0; }
public class ShipAbility : UnityEngine.MonoBehaviour { }
public class DefenseFormation { }
