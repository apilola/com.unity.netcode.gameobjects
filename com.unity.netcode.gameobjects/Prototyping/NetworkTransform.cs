using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Unity.Netcode.Prototyping
{
    /// <summary>
    /// A prototype component for syncing transforms
    /// </summary>
    [AddComponentMenu("Netcode/" + nameof(NetworkTransform))]
    [DefaultExecutionOrder(1000)] // this is needed to catch the update time after the transform was updated by user scripts
    public class NetworkTransform : NetworkBehaviour
    {
        internal struct NetworkState : INetworkSerializable
        {
            internal const int InLocalSpaceBit = 0;
            internal const int PositionXBit = 1;
            internal const int PositionYBit = 2;
            internal const int PositionZBit = 3;
            internal const int RotAngleXBit = 4;
            internal const int RotAngleYBit = 5;
            internal const int RotAngleZBit = 6;
            internal const int ScaleXBit = 7;
            internal const int ScaleYBit = 8;
            internal const int ScaleZBit = 9;

            // 10-15: <unused>
            public ushort Bitset;

            public bool InLocalSpace
            {
                get => (Bitset & (1 << InLocalSpaceBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << InLocalSpaceBit);
            }

            // Position
            public bool HasPositionX
            {
                get => (Bitset & (1 << PositionXBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << PositionXBit);
            }

            public bool HasPositionY
            {
                get => (Bitset & (1 << PositionYBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << PositionYBit);
            }

            public bool HasPositionZ
            {
                get => (Bitset & (1 << PositionZBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << PositionZBit);
            }

            // RotAngles
            public bool HasRotAngleX
            {
                get => (Bitset & (1 << RotAngleXBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << RotAngleXBit);
            }

            public bool HasRotAngleY
            {
                get => (Bitset & (1 << RotAngleYBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << RotAngleYBit);
            }

            public bool HasRotAngleZ
            {
                get => (Bitset & (1 << RotAngleZBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << RotAngleZBit);
            }

            // Scale
            public bool HasScaleX
            {
                get => (Bitset & (1 << ScaleXBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << ScaleXBit);
            }

            public bool HasScaleY
            {
                get => (Bitset & (1 << ScaleYBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << ScaleYBit);
            }

            public bool HasScaleZ
            {
                get => (Bitset & (1 << ScaleZBit)) != 0;
                set => Bitset |= (ushort) ((value ? 1 : 0) << ScaleZBit);
            }

            public float PositionX, PositionY, PositionZ;
            public float RotAngleX, RotAngleY, RotAngleZ;
            public float ScaleX, ScaleY, ScaleZ;
            public double SentTime;

            public Vector3 Position
            {
                get { return new Vector3(PositionX, PositionY, PositionZ); }
                set
                {
                    PositionX = value.x;
                    PositionY = value.y;
                    PositionZ = value.z;
                }
            }

            public Vector3 Rotation
            {
                get { return new Vector3(RotAngleX, RotAngleY, RotAngleZ); }
                set
                {
                    RotAngleX = value.x;
                    RotAngleY = value.y;
                    RotAngleZ = value.z;
                }
            }

            public Vector3 Scale
            {
                get { return new Vector3(ScaleX, ScaleY, ScaleZ); }
                set
                {
                    ScaleX = value.x;
                    ScaleY = value.y;
                    ScaleZ = value.z;
                }
            }

            public void NetworkSerialize(NetworkSerializer serializer)
            {
                serializer.Serialize(ref SentTime);
                // InLocalSpace + HasXXX Bits
                serializer.Serialize(ref Bitset);
                // Position Values
                if (HasPositionX)
                {
                    serializer.Serialize(ref PositionX);
                }

                if (HasPositionY)
                {
                    serializer.Serialize(ref PositionY);
                }

                if (HasPositionZ)
                {
                    serializer.Serialize(ref PositionZ);
                }

                // RotAngle Values
                if (HasRotAngleX)
                {
                    serializer.Serialize(ref RotAngleX);
                }

                if (HasRotAngleY)
                {
                    serializer.Serialize(ref RotAngleY);
                }

                if (HasRotAngleZ)
                {
                    serializer.Serialize(ref RotAngleZ);
                }

                // Scale Values
                if (HasScaleX)
                {
                    serializer.Serialize(ref ScaleX);
                }

                if (HasScaleY)
                {
                    serializer.Serialize(ref ScaleY);
                }

                if (HasScaleZ)
                {
                    serializer.Serialize(ref ScaleZ);
                }
            }
        }

        public bool SyncPositionX = true, SyncPositionY = true, SyncPositionZ = true;
        public bool SyncRotAngleX = true, SyncRotAngleY = true, SyncRotAngleZ = true;
        public bool SyncScaleX = true, SyncScaleY = true, SyncScaleZ = true;

        public float PositionThreshold, RotAngleThreshold, ScaleThreshold;

        /// <summary>
        /// Sets whether this transform should sync in local space or in world space.
        /// This is important to set since reparenting this transform could have issues,
        /// if using world position (depending on who gets synced first: the parent or the child)
        /// Having a child always at position 0,0,0 for example will have less possibilities of desync than when using world positions
        /// </summary>
        [Tooltip("Sets whether this transform should sync in local space or in world space")]
        public bool InLocalSpace = false;

        public bool Interpolate = true;

        /// <summary>
        /// The base amount of sends per seconds to use when range is disabled
        /// </summary>
        [SerializeField, Range(0, 120), Tooltip("The base amount of sends per seconds to use when range is disabled")]
        public float FixedSendsPerSecond = 30f;

        public bool UseFixedUpdate = true;

        private const int k_DebugDrawLineTime = 10;

        protected virtual IInterpolator<float> PositionXInterpolator { get; set; }
        protected virtual IInterpolator<float> PositionYInterpolator { get; set; }
        protected virtual IInterpolator<float> PositionZInterpolator { get; set; }
        protected virtual IInterpolator<Quaternion> RotationInterpolator { get; set; } // rotation is a single Quaternion since each euler axis will affect the quaternion's final value
        protected virtual IInterpolator<float> ScaleXInterpolator { get; set; }
        protected virtual IInterpolator<float> ScaleYInterpolator { get; set; }
        protected virtual IInterpolator<float> ScaleZInterpolator { get; set; }

        public void InitializeInterpolator<TPos, TRot, TScale>() where TPos : IInterpolator<float>, new() where TRot : IInterpolator<Quaternion>, new() where TScale : IInterpolator<float>, new()
        {
            m_AllFloatInterpolators.Clear();
            if (Interpolate)
            {
                PositionXInterpolator = new TPos();
                PositionYInterpolator = new TPos();
                PositionZInterpolator = new TPos();
                RotationInterpolator = new TRot();
                ScaleXInterpolator = new TScale();
                ScaleYInterpolator = new TScale();
                ScaleZInterpolator = new TScale();
            }
            else
            {
                PositionXInterpolator = new NoInterpolator<float>();
                PositionYInterpolator = new NoInterpolator<float>();
                PositionZInterpolator = new NoInterpolator<float>();
                RotationInterpolator = new NoInterpolator<Quaternion>();
                ScaleXInterpolator = new NoInterpolator<float>();
                ScaleYInterpolator = new NoInterpolator<float>();
                ScaleZInterpolator = new NoInterpolator<float>();
            }

            m_AllFloatInterpolators.Add(PositionXInterpolator);
            m_AllFloatInterpolators.Add(PositionYInterpolator);
            m_AllFloatInterpolators.Add(PositionZInterpolator);
            m_AllFloatInterpolators.Add(ScaleXInterpolator);
            m_AllFloatInterpolators.Add(ScaleYInterpolator);
            m_AllFloatInterpolators.Add(ScaleZInterpolator);
        }

        private readonly List<IInterpolator<float>> m_AllFloatInterpolators = new List<IInterpolator<float>>(6);

        private Transform m_Transform; // cache the transform component to reduce unnecessary bounce between managed and native

        internal readonly NetworkVariable<NetworkState> ReplNetworkState = new NetworkVariable<NetworkState>(new NetworkState());
        internal NetworkState PrevNetworkState;
        internal NetworkState LocalAuthoritativeNetworkState;

        public void ResetCurrentInterpolatedState()
        {
            PositionXInterpolator.ResetTo(ReplNetworkState.Value.PositionX);
            PositionYInterpolator.ResetTo(ReplNetworkState.Value.PositionY);
            PositionZInterpolator.ResetTo(ReplNetworkState.Value.PositionZ);

            RotationInterpolator.ResetTo(Quaternion.Euler(ReplNetworkState.Value.Rotation));

            ScaleXInterpolator.ResetTo(ReplNetworkState.Value.ScaleX);
            ScaleYInterpolator.ResetTo(ReplNetworkState.Value.ScaleY);
            ScaleZInterpolator.ResetTo(ReplNetworkState.Value.ScaleZ);
        }

        // updates `NetworkState` properties if they need to and returns a `bool` indicating whether or not there was any changes made
        // returned boolean would be useful to change encapsulating `NetworkVariable<NetworkState>`'s dirty state, e.g. ReplNetworkState.SetDirty(isDirty);
        internal bool UpdateNetworkStateCheckDirty(ref NetworkState networkState, double dirtyTime)
        {
            return UpdateNetworkStateCheckDirtyWithInfo(ref networkState, dirtyTime).isDirty;
        }

        private (bool isDirty, bool isPositionDirty, bool isRotationDirty, bool isScaleDirty) UpdateNetworkStateCheckDirtyWithInfo(ref NetworkState networkState, double dirtyTime)
        {
            var position = InLocalSpace ? m_Transform.localPosition : m_Transform.position;
            var rotAngles = InLocalSpace ? m_Transform.localEulerAngles : m_Transform.eulerAngles;
            var scale = InLocalSpace ? m_Transform.localScale : m_Transform.lossyScale;

            bool isDirty = false;
            bool isPositionDirty = false;
            bool isRotationDirty = false;
            bool isScaleDirty = false;

            if (InLocalSpace != networkState.InLocalSpace)
            {
                networkState.InLocalSpace = InLocalSpace;
                isDirty = true;
            }

            if (SyncPositionX &&
                Mathf.Abs(networkState.PositionX - position.x) >= PositionThreshold &&
                !Mathf.Approximately(networkState.PositionX, position.x))
            {
                networkState.PositionX = position.x;
                networkState.HasPositionX = true;
                isPositionDirty = true;
            }

            if (SyncPositionY &&
                Mathf.Abs(networkState.PositionY - position.y) >= PositionThreshold &&
                !Mathf.Approximately(networkState.PositionY, position.y))
            {
                networkState.PositionY = position.y;
                networkState.HasPositionY = true;
                isPositionDirty = true;
            }

            if (SyncPositionZ &&
                Mathf.Abs(networkState.PositionZ - position.z) >= PositionThreshold &&
                !Mathf.Approximately(networkState.PositionZ, position.z))
            {
                networkState.PositionZ = position.z;
                networkState.HasPositionZ = true;
                isPositionDirty = true;
            }

            if (SyncRotAngleX &&
                Mathf.Abs(networkState.RotAngleX - rotAngles.x) >= RotAngleThreshold &&
                !Mathf.Approximately(networkState.RotAngleX, rotAngles.x))
            {
                networkState.RotAngleX = rotAngles.x;
                networkState.HasRotAngleX = true;
                isRotationDirty = true;
            }

            if (SyncRotAngleY &&
                Mathf.Abs(networkState.RotAngleY - rotAngles.y) >= RotAngleThreshold &&
                !Mathf.Approximately(networkState.RotAngleY, rotAngles.y))
            {
                networkState.RotAngleY = rotAngles.y;
                networkState.HasRotAngleY = true;
                isRotationDirty = true;
            }

            if (SyncRotAngleZ &&
                Mathf.Abs(networkState.RotAngleZ - rotAngles.z) >= RotAngleThreshold &&
                !Mathf.Approximately(networkState.RotAngleZ, rotAngles.z))
            {
                networkState.RotAngleZ = rotAngles.z;
                networkState.HasRotAngleZ = true;
                isRotationDirty = true;
            }

            if (SyncScaleX &&
                Mathf.Abs(networkState.ScaleX - scale.x) >= ScaleThreshold &&
                !Mathf.Approximately(networkState.ScaleX, scale.x))
            {
                networkState.ScaleX = scale.x;
                networkState.HasScaleX = true;
                isScaleDirty = true;
            }

            if (SyncScaleY &&
                Mathf.Abs(networkState.ScaleY - scale.y) >= ScaleThreshold &&
                !Mathf.Approximately(networkState.ScaleY, scale.y))
            {
                networkState.ScaleY = scale.y;
                networkState.HasScaleY = true;
                isScaleDirty = true;
            }

            if (SyncScaleZ &&
                Mathf.Abs(networkState.ScaleZ - scale.z) >= ScaleThreshold &&
                !Mathf.Approximately(networkState.ScaleZ, scale.z))
            {
                networkState.ScaleZ = scale.z;
                networkState.HasScaleZ = true;
                isScaleDirty = true;
            }

            isDirty |= isPositionDirty || isRotationDirty || isScaleDirty;

            if (isDirty)
            {
                networkState.SentTime = dirtyTime;
            }


            return (isDirty, isPositionDirty, isRotationDirty, isScaleDirty);
        }

        internal void ApplyNetworkStateFromAuthority(NetworkState networkState)
        {
            PrevNetworkState = networkState;

            var interpolatedPosition = InLocalSpace ? m_Transform.localPosition : m_Transform.position;
            var interpolatedRotAngles = InLocalSpace ? m_Transform.localEulerAngles : m_Transform.eulerAngles;
            var interpolatedScale = InLocalSpace ? m_Transform.localScale : m_Transform.lossyScale;

            // InLocalSpace Read
            InLocalSpace = networkState.InLocalSpace;
            // Position Read
            if (networkState.HasPositionX)
            {
                interpolatedPosition.x = PositionXInterpolator.GetInterpolatedValue();
            }

            if (networkState.HasPositionY)
            {
                interpolatedPosition.y = PositionYInterpolator.GetInterpolatedValue();
            }

            if (networkState.HasPositionZ)
            {
                interpolatedPosition.z = PositionZInterpolator.GetInterpolatedValue();
            }

            if (networkState.HasRotAngleX)
            {
                interpolatedRotAngles.x = RotationInterpolator.GetInterpolatedValue().eulerAngles.x;
            }

            if (networkState.HasRotAngleY)
            {
                interpolatedRotAngles.y = RotationInterpolator.GetInterpolatedValue().eulerAngles.y;
            }

            if (networkState.HasRotAngleZ)
            {
                interpolatedRotAngles.z = RotationInterpolator.GetInterpolatedValue().eulerAngles.z;
            }

            // Scale Read
            if (networkState.HasScaleX)
            {
                interpolatedScale.x = ScaleXInterpolator.GetInterpolatedValue();
            }

            if (networkState.HasScaleY)
            {
                interpolatedScale.y = ScaleYInterpolator.GetInterpolatedValue();
            }

            if (networkState.HasScaleZ)
            {
                interpolatedScale.z = ScaleZInterpolator.GetInterpolatedValue();
            }

            // Position Apply
            if (networkState.HasPositionX || networkState.HasPositionY || networkState.HasPositionZ)
            {
                if (InLocalSpace)
                {
                    m_Transform.localPosition = interpolatedPosition;
                }
                else
                {
                    m_Transform.position = interpolatedPosition;
                }

                PrevNetworkState.Position = interpolatedPosition;
            }

            // RotAngles Apply
            if (networkState.HasRotAngleX || networkState.HasRotAngleY || networkState.HasRotAngleZ)
            {
                if (InLocalSpace)
                {
                    m_Transform.localRotation = Quaternion.Euler(interpolatedRotAngles);
                }
                else
                {
                    m_Transform.rotation = Quaternion.Euler(interpolatedRotAngles);
                }

                PrevNetworkState.Rotation = interpolatedRotAngles;
            }

            // Scale Apply
            if (networkState.HasScaleX || networkState.HasScaleY || networkState.HasScaleZ)
            {
                if (InLocalSpace)
                {
                    m_Transform.localScale = interpolatedScale;
                }
                else
                {
                    m_Transform.localScale = Vector3.one;
                    var lossyScale = m_Transform.lossyScale;
                    // todo this conversion is messing with interpolation. local scale interpolates fine, lossy scale is jittery. must investigate
                    m_Transform.localScale = new Vector3(networkState.ScaleX / lossyScale.x, networkState.ScaleY / lossyScale.y, networkState.ScaleZ / lossyScale.z);
                }

                PrevNetworkState.Scale = interpolatedScale;
            }
        }

        private void OnNetworkStateChanged(NetworkState oldState, NetworkState newState)
        {
            if (!NetworkObject.IsSpawned)
            {
                // todo MTT-849 should never happen but yet it does! maybe revisit/dig after NetVar updates and snapshot system lands?
                return;
            }

            if (IsServer)
            {
                return; // todo use authority
            }

            var sentTime = new NetworkTime(NetworkManager.Singleton.ServerTime.TickRate, newState.SentTime);

            if (newState.HasPositionX)
            {
                PositionXInterpolator.AddMeasurement(newState.PositionX, sentTime);
            }

            if (newState.HasPositionY)
            {
                PositionYInterpolator.AddMeasurement(newState.PositionY, sentTime);
            }

            if (newState.HasPositionZ)
            {
                PositionZInterpolator.AddMeasurement(newState.PositionZ, sentTime);
            }

            RotationInterpolator.AddMeasurement(Quaternion.Euler(newState.Rotation), sentTime);

            if (newState.HasScaleX)
            {
                ScaleXInterpolator.AddMeasurement(newState.ScaleX, sentTime);
            }

            if (newState.HasScaleY)
            {
                ScaleYInterpolator.AddMeasurement(newState.ScaleY, sentTime);
            }

            if (newState.HasScaleZ)
            {
                ScaleZInterpolator.AddMeasurement(newState.ScaleZ, sentTime);
            }

            if (NetworkManager.Singleton.LogLevel == LogLevel.Developer)
            {
                var pos = new Vector3(newState.PositionX, newState.PositionY, newState.PositionZ);
                Debug.DrawLine(pos, pos + Vector3.up + Vector3.left * Random.Range(0.5f, 2f), Color.green, k_DebugDrawLineTime, false);
            }
        }

        private void Awake()
        {
            m_Transform = transform;

            if (m_AllFloatInterpolators.Count == 0)
            {
                InitializeInterpolator<BufferedLinearInterpolatorFloat, BufferedLinearInterpolatorQuaternion, BufferedLinearInterpolatorFloatForScale>();
            }

            foreach (var interpolator in m_AllFloatInterpolators)
            {
                interpolator.Awake();
                interpolator.UseFixedUpdate = UseFixedUpdate;
            }

            RotationInterpolator.Awake();
            RotationInterpolator.UseFixedUpdate = UseFixedUpdate;

            // ReplNetworkState.NetworkVariableChannel = NetworkChannel.PositionUpdate; // todo figure this out, talk with Matt/Fatih, this should be unreliable

            // set initial value for spawn
            if (IsServer)
            {
                if (UseFixedUpdate)
                {
                    // try to update local NetworkState
                    DoUpdateToGhosts(NetworkManager.LocalTime.FixedTime);
                }
                else
                {
                    DoUpdateToGhosts(NetworkManager.LocalTime.Time);
                }
            }

            ReplNetworkState.OnValueChanged += OnNetworkStateChanged;
        }

        public void Start()
        {
            foreach (var interpolator in m_AllFloatInterpolators)
            {
                interpolator.Start();
            }

            RotationInterpolator.Start();
        }

        public void OnEnable()
        {
            foreach (var interpolator in m_AllFloatInterpolators)
            {
                interpolator.OnEnable();
            }

            RotationInterpolator.OnEnable();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                ResetCurrentInterpolatedState(); // useful for late joining

                foreach (var interpolator in m_AllFloatInterpolators)
                {
                    interpolator.OnNetworkSpawn();
                }

                // todo cache network manager to remove some of the time it takes to get it
                RotationInterpolator.OnNetworkSpawn();

                ApplyNetworkStateFromAuthority(ReplNetworkState.Value);
            }
        }

        private void OnDestroy()
        {
            ReplNetworkState.OnValueChanged -= OnNetworkStateChanged;

            foreach (var interpolator in m_AllFloatInterpolators)
            {
                interpolator.OnDestroy();
            }

            RotationInterpolator.OnDestroy();
        }

        private void DoUpdateToGhosts(double time)
        {
            if (UpdateNetworkStateCheckDirty(ref LocalAuthoritativeNetworkState, time))
            {
                ReplNetworkState.Value = LocalAuthoritativeNetworkState;
                ReplNetworkState.SetDirty(true);
            }
        }

        private void FixedUpdate()
        {
            if (!NetworkObject.IsSpawned)
            {
                return;
            }

            if (IsServer && UseFixedUpdate) // todo change IsServer for HasAuthority check
            {
                // try to update local NetworkState
                DoUpdateToGhosts(NetworkManager.LocalTime.FixedTime);
            }

            // try to update previously consumed NetworkState
            // if we have any changes, that means made some updates locally
            // we apply the latest ReplNetworkState again to revert our changes
            if (!IsServer)
            {
                var oldStateDirtyInfo = UpdateNetworkStateCheckDirtyWithInfo(ref PrevNetworkState, 0);
                if (oldStateDirtyInfo.isDirty && !oldStateDirtyInfo.isRotationDirty)
                {
                    // ignoring rotation dirty since quaternions will mess with euler angles, making this impossible to determine if the change to a single axis comes
                    // from an unauthorized transform change or euler to quaternion conversion artifacts.
                    var dirtyField = oldStateDirtyInfo.isPositionDirty ? "position" : "scale";
                    Debug.LogWarning($"A local change to {dirtyField} without authority detected, reverting back to latest interpolated network state!", this);
                    ApplyNetworkStateFromAuthority(ReplNetworkState.Value);
                }

                foreach (var interpolator in m_AllFloatInterpolators)
                {
                    interpolator.FixedUpdate(NetworkManager.ServerTime.FixedDeltaTime);
                }

                RotationInterpolator.FixedUpdate(NetworkManager.ServerTime.FixedDeltaTime);
            }
        }

        private void Update()
        {
            if (!NetworkObject.IsSpawned)
            {
                return;
            }

            if (IsServer && !UseFixedUpdate)
            {
                DoUpdateToGhosts(NetworkManager.LocalTime.Time);
            }

            // apply interpolated value
            if (!IsServer && (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsListening))
            {
                foreach (var interpolator in m_AllFloatInterpolators)
                {
                    interpolator.Update(Time.deltaTime);
                }

                RotationInterpolator.Update(Time.deltaTime);

                if (NetworkManager.Singleton.LogLevel == LogLevel.Developer)
                {
                    var interpolatedPosition = new Vector3(PositionXInterpolator.GetInterpolatedValue(), PositionYInterpolator.GetInterpolatedValue(), PositionZInterpolator.GetInterpolatedValue());
                    Debug.DrawLine(interpolatedPosition, interpolatedPosition + Vector3.up, Color.magenta, k_DebugDrawLineTime, false);
                }

                ApplyNetworkStateFromAuthority(ReplNetworkState.Value);
            }
        }

        /// <summary>
        /// Teleports the transform to the given values without interpolating
        /// </summary>
        public void Teleport(Vector3 newPosition, Quaternion newRotation, Vector3 newScale)
        {
            // check server side
            // set teleport flag in state
            throw new NotImplementedException(); // TODO MTT-769
        }
    }
}