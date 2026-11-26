using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет чтением письма Матвея (Letter_Sheet.001 + Letter_Armature.001) с выразительной
    /// «ручной» анимацией (дуги Безье, anticipation, overlapping action, follow-through, живое дыхание руки).
    /// - При взятии: мягко вытягивает сложенное письмо из-за фото/коммутатора вниз-вбок, несёт по дуге к глазам
    ///   и внахлёст разворачивает лист под звук шуршания бумаги, размывая фон (DoF).
    /// - При возврате: складывает лист, чуть опускает его, заводит сзади-снизу за коммутатор и аккуратно
    ///   поднимает на прежнее место за фотографией, чтобы письмо не проходило сквозь фото.
    /// </summary>
    public sealed class LetterInspectController : MonoBehaviour
    {
        private enum LetterMotionState
        {
            Resting = 0,
            PickingUp = 1,
            Reading = 2,
            PuttingBack = 3
        }

        [Header("Ссылки на объекты письма")]
        [SerializeField] private Transform letterSheetTransform;
        [SerializeField] private Transform letterArmatureTransform;
        [SerializeField] private AnimationClip foldAnimationClip;

        [Header("Ссылки на системы сцены")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;
        [SerializeField] private Volume backgroundBlurVolume;
        [SerializeField, Range(0f, 1f)] private float maxBlurWeight = 1f;

        [Header("Поза чтения перед взглядом")]
        [SerializeField] private Transform inspectPoseTarget;
        [SerializeField, Min(0.15f)] private float fallbackDistanceFromCamera = 0.295f;
        [SerializeField] private float fallbackVerticalOffset = -0.012f;
        [SerializeField, Range(-30f, 30f)] private float fallbackTiltDegrees = 18f;

        [Header("Хореография руки (12 принципов анимации)")]
        [Tooltip("Общая длительность взятия и разворачивания письма (сек).")]
        [SerializeField, Min(0.4f)] private float pickUpTotalDuration = 1.25f;
        [Tooltip("Общая длительность складывания, опускания и заведения за коммутатор (сек).")]
        [SerializeField, Min(0.5f)] private float putBackTotalDuration = 1.45f;

        [Tooltip("Смещение точки безопасного входа/выхода снизу-сзади за коммутатором (мировые координаты от homePosition), чтобы не задевать фото.")]
        [SerializeField] private Vector3 tuckBehindOffset = new Vector3(-0.008f, -0.092f, -0.036f);

        [Tooltip("Смещение внешней дуги руки при переносе письма между столом и глазами.")]
        [SerializeField] private Vector3 handCarryArcOffset = new Vector3(-0.07f, -0.045f, 0.085f);

        [Tooltip("Лёгкое живое дыхание руки, пока игрок читает письмо (метры и градусы).")]
        [SerializeField, Range(0f, 0.006f)] private float handBreathingPositionMeters = 0.0014f;
        [SerializeField, Range(0f, 1.5f)] private float handBreathingAngleDegrees = 0.35f;

        [Header("Остаточное движение камеры (градусы)")]
        [SerializeField, Range(0f, 15f)] private float focusMaxHorizontalAngle = 5f;
        [SerializeField, Range(0f, 15f)] private float focusMaxVerticalAngle = 4f;

        [Header("Звук шуршания бумаги")]
        [SerializeField] private AudioSource paperAudioSource;
        [SerializeField] private AudioClip customPaperRustleClip;
        [SerializeField, Range(0f, 1f)] private float rustleVolume = 0.42f;

        [Header("Взаимодействие")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;
        [SerializeField] private bool closeOnRightClick = true;
        [SerializeField] private bool closeOnEscape = true;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _inspectPosition;
        private Quaternion _inspectRotation;
        private bool _homeCaptured;

        private LetterMotionState _state = LetterMotionState.Resting;
        private float _stateTime;
        private float _currentUnfoldAmount;
        private float _currentBlurAmount;
        private float _readingTime;

        // Для плавного перехвата, если игрок передумал прямо посреди анимации.
        private Vector3 _interruptStartPos;
        private Quaternion _interruptStartRot;
        private float _interruptStartUnfold;
        private bool _startedFromInterrupt;

        private AudioClip _proceduralRustleClip;

        public bool IsReading => _state == LetterMotionState.PickingUp || _state == LetterMotionState.Reading;

        private void Awake()
        {
            if (letterSheetTransform == null)
            {
                letterSheetTransform = transform;
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            if (cameraLook == null && interactionCamera != null)
            {
                cameraLook = interactionCamera.GetComponent<SmoothMouseLookCamera>();
            }

            if (letterArmatureTransform != null)
            {
                Animator anim = letterArmatureTransform.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.enabled = false;
                }
            }

            EnsureAudioSource();
        }

        private void Start()
        {
            CaptureHomePoseIfNeeded();
            _inspectPosition = _homePosition;
            _inspectRotation = _homeRotation;
            _state = LetterMotionState.Resting;
            _currentUnfoldAmount = 0f;
            _currentBlurAmount = 0f;
            SampleFoldPose(0f);
        }

        private void OnEnable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged += HandleCameraFocusOwnerChanged;
            }
        }

        private void OnDisable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged -= HandleCameraFocusOwnerChanged;
            }
        }

        private void OnDestroy()
        {
            if (_proceduralRustleClip != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_proceduralRustleClip);
                }
                else
                {
                    DestroyImmediate(_proceduralRustleClip);
                }
                _proceduralRustleClip = null;
            }
        }

        private void Update()
        {
            UpdateAnimationStateMachine();

            if (cameraLook == null || GameplayInputGate.IsBlocked)
            {
                return;
            }

            if (switchboardInteractor != null && switchboardInteractor.ActivePlug != null)
            {
                return;
            }

            if (IsReading)
            {
                if ((closeOnEscape && WasEscapePressed()) ||
                    (closeOnRightClick && WasRightMousePressed()))
                {
                    PutLetterBack();
                }

                return;
            }

            if (_state == LetterMotionState.Resting &&
                WasLeftMousePressed(out Vector2 mousePos) &&
                !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) &&
                IsClickOnLetter(mousePos))
            {
                PickUpAndUnfoldLetter();
            }
        }

        /// <summary>
        /// Плавно вытягивает письмо из-за фотографии, несёт рукой по дуге к глазам и разворачивает.
        /// </summary>
        public void PickUpAndUnfoldLetter()
        {
            CaptureHomePoseIfNeeded();

            if (inspectPoseTarget != null)
            {
                _inspectPosition = inspectPoseTarget.position;
                _inspectRotation = inspectPoseTarget.rotation;
            }
            else if (cameraLook != null)
            {
                ComputeFixedInspectPose(out _inspectPosition, out _inspectRotation);
            }

            _startedFromInterrupt = _state != LetterMotionState.Resting;
            _interruptStartPos = letterSheetTransform.position;
            _interruptStartRot = letterSheetTransform.rotation;
            _interruptStartUnfold = _currentUnfoldAmount;

            _state = LetterMotionState.PickingUp;
            _stateTime = 0f;
            _readingTime = 0f;

            PlayPaperRustleSound(unfolding: true);

            if (cameraLook != null)
            {
                cameraLook.FocusOn(
                    cameraLook.DefaultWorldPosition,
                    cameraLook.DefaultWorldRotation,
                    focusMaxHorizontalAngle,
                    focusMaxVerticalAngle,
                    owner: this);
            }
        }

        /// <summary>
        /// Складывает письмо, чуть опускает, заводит за коммутатор снизу и поднимает на прежнее место за фотографией.
        /// </summary>
        public void PutLetterBack()
        {
            CaptureHomePoseIfNeeded();
            if (_state == LetterMotionState.Resting || _state == LetterMotionState.PuttingBack)
            {
                return;
            }

            _startedFromInterrupt = _state == LetterMotionState.PickingUp;
            _interruptStartPos = letterSheetTransform.position;
            _interruptStartRot = letterSheetTransform.rotation;
            _interruptStartUnfold = _currentUnfoldAmount;

            _state = LetterMotionState.PuttingBack;
            _stateTime = 0f;

            PlayPaperRustleSound(unfolding: false);

            if (cameraLook != null)
            {
                cameraLook.ClearFocus(owner: this);
            }
        }

        private void UpdateAnimationStateMachine()
        {
            if (!_homeCaptured)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;

            switch (_state)
            {
                case LetterMotionState.PickingUp:
                    UpdatePickUpAnimation(dt);
                    break;

                case LetterMotionState.Reading:
                    UpdateReadingHandBreathing(dt);
                    break;

                case LetterMotionState.PuttingBack:
                    UpdatePutBackAnimation(dt);
                    break;
            }
        }

        private void UpdatePickUpAnimation(float dt)
        {
            _stateTime += dt;
            float u = Mathf.Clamp01(_stateTime / Mathf.Max(0.2f, pickUpTotalDuration));

            Vector3 startPos = _startedFromInterrupt ? _interruptStartPos : _homePosition;
            Quaternion startRot = _startedFromInterrupt ? _interruptStartRot : _homeRotation;

            // Точка выхода из-за фотографии (чуть ниже и правее сзади, чтобы не цеплять Photo.001).
            Vector3 slideOutPos = _homePosition + tuckBehindOffset;
            Quaternion slideOutRot = Quaternion.Slerp(_homeRotation, _inspectRotation, 0.22f) * Quaternion.Euler(0f, -10f, -6f);

            // Контрольная точка мягкой ручной дуги (Arcs).
            Vector3 arcMid = Vector3.Lerp(slideOutPos, _inspectPosition, 0.55f) + handCarryArcOffset;

            Vector3 pos;
            Quaternion rot;

            if (!_startedFromInterrupt && u < 0.26f)
            {
                // Фаза 1 (Anticipation + аккуратное вытягивание из-за фото вниз-вбок).
                float p = SmootherStep(u / 0.26f);
                Vector3 dipCtrl = Vector3.Lerp(startPos, slideOutPos, 0.5f) + new Vector3(-0.01f, -0.02f, -0.008f);
                pos = EvalQuadraticBezier(startPos, dipCtrl, slideOutPos, p);
                rot = Quaternion.Slerp(startRot, slideOutRot, p);
            }
            else
            {
                // Фаза 2 (Основная дуга рукой к глазам + мягкий Follow-Through & Settle на финише).
                float pRaw = _startedFromInterrupt ? u : Mathf.InverseLerp(0.26f, 1.0f, u);
                float p = SmootherStep(pRaw);

                Vector3 fromPos = _startedFromInterrupt ? startPos : slideOutPos;
                Quaternion fromRot = _startedFromInterrupt ? startRot : slideOutRot;

                pos = EvalQuadraticBezier(fromPos, arcMid, _inspectPosition, p);

                // Лёгкий естественный перелёт и усадка кисти (Follow-Through) в последние 25%.
                float settleWave = Mathf.Sin(Mathf.InverseLerp(0.65f, 1f, pRaw) * Mathf.PI) * (1f - pRaw) * 2.5f;
                pos += Vector3.up * (settleWave * 0.0035f);

                // Промежуточный живой поворот кисти во время переноса.
                Quaternion wristRoll = Quaternion.Euler(Mathf.Sin(p * Mathf.PI) * -7f, Mathf.Sin(p * Mathf.PI) * 8f, 0f);
                rot = Quaternion.Slerp(fromRot, _inspectRotation, p) * wristRoll;
            }

            // Overlapping Action: бумага начинает разворачиваться внахлёст во время подлёта и расправляется чуть после прилёта.
            float unfoldT = SmootherStep(Mathf.InverseLerp(0.32f, 0.96f, u));
            _currentUnfoldAmount = Mathf.Lerp(_startedFromInterrupt ? _interruptStartUnfold : 0f, 1f, unfoldT);
            SampleFoldPose(_currentUnfoldAmount);

            // Размытие фона (DoF) плавно нарастает вместе с приближением письма к глазам.
            _currentBlurAmount = SmootherStep(Mathf.InverseLerp(0.10f, 0.85f, u));
            ApplyBlurWeight(_currentBlurAmount);

            ApplyTransformPose(pos, rot);

            if (u >= 1f)
            {
                _state = LetterMotionState.Reading;
                _readingTime = 0f;
                _currentUnfoldAmount = 1f;
                SampleFoldPose(1f);
                ApplyBlurWeight(1f);
                ApplyTransformPose(_inspectPosition, _inspectRotation);
            }
        }

        private void UpdateReadingHandBreathing(float dt)
        {
            _readingTime += dt;
            if (handBreathingPositionMeters <= 0f && handBreathingAngleDegrees <= 0f)
            {
                return;
            }

            // Мягко нарастающее незаметное дыхание руки (чтобы письмо не было «каменным» в воздухе).
            float fadeIn = Mathf.Clamp01(_readingTime / 0.8f);
            float breathY = Mathf.Sin(_readingTime * 1.45f) * handBreathingPositionMeters * fadeIn;
            float breathX = Mathf.Cos(_readingTime * 0.92f) * handBreathingPositionMeters * 0.55f * fadeIn;
            float swayAngle = Mathf.Sin(_readingTime * 1.1f) * handBreathingAngleDegrees * fadeIn;

            Vector3 pos = _inspectPosition + new Vector3(breathX, breathY, 0f);
            Quaternion rot = _inspectRotation * Quaternion.Euler(swayAngle * 0.6f, swayAngle, 0f);
            ApplyTransformPose(pos, rot);
        }

        private void UpdatePutBackAnimation(float dt)
        {
            _stateTime += dt;
            float u = Mathf.Clamp01(_stateTime / Mathf.Max(0.25f, putBackTotalDuration));

            Vector3 startPos = _startedFromInterrupt ? _interruptStartPos : _inspectPosition;
            Quaternion startRot = _startedFromInterrupt ? _interruptStartRot : _inspectRotation;
            float startUnfold = _startedFromInterrupt ? _interruptStartUnfold : 1f;

            // Ключевые точки маршрута возврата (строго обходя фотографию снизу-сзади):
            // P1: чуть опущенное письмо перед собой (после складывания)
            Vector3 loweredFrontPos = startPos + new Vector3(-0.04f, -0.055f, -0.035f);
            Quaternion loweredFrontRot = Quaternion.Slerp(startRot, _homeRotation, 0.35f) * Quaternion.Euler(8f, -10f, 0f);

            // P2: точка ниже и ЗА коммутатором / фотографией (отсюда письмо поднимается на своё место!)
            Vector3 behindBelowPos = _homePosition + tuckBehindOffset;
            Quaternion behindBelowRot = Quaternion.Slerp(_homeRotation, loweredFrontRot, 0.12f);

            Vector3 pos;
            Quaternion rot;

            if (u < 0.32f)
            {
                // Фаза 1 (0..32%): Складываем письмо в руках и чуть опускаем его вниз (Anticipation).
                float p = SmootherStep(u / 0.32f);
                Vector3 antCtrl = startPos + new Vector3(-0.01f, 0.012f, 0.01f); // крошечный замах вверх-на-себя перед опусканием
                pos = EvalQuadraticBezier(startPos, antCtrl, loweredFrontPos, p);
                rot = Quaternion.Slerp(startRot, loweredFrontRot, p);
            }
            else if (u < 0.72f)
            {
                // Фаза 2 (32..72%): Опускаем письмо и по широкой дуге заводим его вправо-назад ЗА коммутатор (ниже фото!).
                float p = SmootherStep((u - 0.32f) / 0.40f);
                Vector3 sweepCtrl = Vector3.Lerp(loweredFrontPos, behindBelowPos, 0.5f) + new Vector3(-0.06f, -0.055f, 0.045f);
                pos = EvalQuadraticBezier(loweredFrontPos, sweepCtrl, behindBelowPos, p);
                rot = Quaternion.Slerp(loweredFrontRot, behindBelowRot, p);
            }
            else
            {
                // Фаза 3 (72..100%): Заведя письмо за коммутатор снизу, мягко поднимаем его вверх на прежнее место за фото!
                float p = SmootherStep((u - 0.72f) / 0.28f);
                Vector3 tuckCtrl = Vector3.Lerp(behindBelowPos, _homePosition, 0.5f) + new Vector3(-0.008f, -0.012f, -0.012f);
                pos = EvalQuadraticBezier(behindBelowPos, tuckCtrl, _homePosition, p);
                rot = Quaternion.Slerp(behindBelowRot, _homeRotation, p);
            }

            // Overlapping Action: складывание завершается аккурат перед заведением за коммутатор.
            float foldT = SmootherStep(Mathf.InverseLerp(0.02f, 0.46f, u));
            _currentUnfoldAmount = Mathf.Lerp(startUnfold, 0f, foldT);
            SampleFoldPose(_currentUnfoldAmount);

            // Размытие фона плавно исчезает в первой половине возврата.
            _currentBlurAmount = 1f - SmootherStep(Mathf.InverseLerp(0.05f, 0.58f, u));
            ApplyBlurWeight(_currentBlurAmount);

            ApplyTransformPose(pos, rot);

            if (u >= 1f)
            {
                _state = LetterMotionState.Resting;
                _currentUnfoldAmount = 0f;
                _currentBlurAmount = 0f;
                SampleFoldPose(0f);
                ApplyBlurWeight(0f);
                ApplyTransformPose(_homePosition, _homeRotation);
            }
        }

        private void ApplyBlurWeight(float normalizedWeight)
        {
            if (backgroundBlurVolume != null &&
                (cameraLook == null || cameraLook.CurrentFocusOwner == null || ReferenceEquals(cameraLook.CurrentFocusOwner, this)))
            {
                backgroundBlurVolume.weight = Mathf.Clamp01(normalizedWeight) * maxBlurWeight;
            }
        }

        private void ApplyTransformPose(Vector3 worldPos, Quaternion worldRot)
        {
            if (letterSheetTransform != null)
            {
                letterSheetTransform.position = worldPos;
                letterSheetTransform.rotation = worldRot;
            }

            if (letterArmatureTransform != null && letterArmatureTransform != letterSheetTransform)
            {
                letterArmatureTransform.position = worldPos;
                letterArmatureTransform.rotation = worldRot;
            }
        }

        private void SampleFoldPose(float unfoldAmount)
        {
            if (foldAnimationClip == null || letterArmatureTransform == null)
            {
                return;
            }

            float clipTime = Mathf.Lerp(foldAnimationClip.length, 0f, Mathf.Clamp01(unfoldAmount));
            foldAnimationClip.SampleAnimation(letterArmatureTransform.gameObject, clipTime);
        }

        private void HandleCameraFocusOwnerChanged(object newOwner)
        {
            if (IsReading && !ReferenceEquals(newOwner, this))
            {
                PutLetterBack();
            }
        }

        private void ComputeFixedInspectPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 camPos = cameraLook.DefaultWorldPosition;
            Quaternion camRot = cameraLook.DefaultWorldRotation;

            Vector3 camFwd = camRot * Vector3.forward;
            Vector3 camUp = camRot * Vector3.up;
            Vector3 camRight = camRot * Vector3.right;

            pos = camPos + camFwd * fallbackDistanceFromCamera + camUp * fallbackVerticalOffset;

            Vector3 innerNormal = Quaternion.AngleAxis(fallbackTiltDegrees, camRight) * (-camFwd);
            innerNormal = Quaternion.AngleAxis(-8f, camUp) * innerNormal;
            rot = Quaternion.LookRotation(innerNormal.normalized, camRight);
        }

        private void CaptureHomePoseIfNeeded()
        {
            if (_homeCaptured || letterSheetTransform == null)
            {
                return;
            }

            _homePosition = letterSheetTransform.position;
            _homeRotation = letterSheetTransform.rotation;
            _homeCaptured = true;
        }

        private bool IsClickOnLetter(Vector2 mousePos)
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.transform == letterSheetTransform ||
                   hit.transform.IsChildOf(letterSheetTransform) ||
                   hit.transform == transform;
        }

        private void PlayPaperRustleSound(bool unfolding)
        {
            EnsureAudioSource();
            if (paperAudioSource == null)
            {
                return;
            }

            if (customPaperRustleClip != null)
            {
                paperAudioSource.pitch = unfolding ? 1.0f : 0.92f;
                paperAudioSource.PlayOneShot(customPaperRustleClip, rustleVolume);
                return;
            }

            if (_proceduralRustleClip == null)
            {
                _proceduralRustleClip = CreatePaperRustleClip();
            }

            paperAudioSource.pitch = unfolding ? 1.04f : 0.88f;
            paperAudioSource.PlayOneShot(_proceduralRustleClip, rustleVolume);
        }

        private void EnsureAudioSource()
        {
            if (paperAudioSource == null)
            {
                paperAudioSource = GetComponent<AudioSource>();
            }

            if (paperAudioSource == null)
            {
                paperAudioSource = gameObject.AddComponent<AudioSource>();
            }

            paperAudioSource.playOnAwake = false;
            paperAudioSource.loop = false;
            paperAudioSource.spatialBlend = 0.25f;
        }

        private static AudioClip CreatePaperRustleClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.72f;
            int count = Mathf.RoundToInt(sampleRate * duration);
            float[] data = new float[count];

            var rng = new System.Random(1944);
            float lp = 0f, hp = 0f, prevLp = 0f;

            for (int i = 0; i < count; i++)
            {
                float u = i / (float)count;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                lp += 0.34f * (white - lp);
                hp = 0.82f * (hp + lp - prevLp);
                prevLp = lp;

                float wave1 = Mathf.Exp(-Mathf.Pow((u - 0.28f) / 0.16f, 2f));
                float wave2 = Mathf.Exp(-Mathf.Pow((u - 0.68f) / 0.20f, 2f)) * 0.85f;
                float env = (wave1 + wave2) * Mathf.Clamp01(u * 16f) * Mathf.Clamp01((1f - u) * 10f);

                data[i] = Mathf.Clamp(hp * env * 0.85f, -0.9f, 0.9f);
            }

            AudioClip clip = AudioClip.Create("Paper_Rustle", count, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static Vector3 EvalQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float inv = 1f - t;
            return inv * inv * p0 + 2f * inv * t * p1 + t * t * p2;
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static bool WasLeftMousePressed(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                mousePosition = Input.mousePosition;
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#endif
        }

        private static bool WasRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private static bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
