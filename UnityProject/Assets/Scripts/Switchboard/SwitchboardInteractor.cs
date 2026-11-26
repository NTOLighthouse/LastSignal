using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Обрабатывает клики и движение мыши для штекеров, портов и доков:
    /// - взятый штекер плавно следует за курсором перед панелью коммутатора;
    /// - жёлтая аниме-обводка включается ТОЛЬКО на том свободном разъёме или доке, на который наведён курсор;
    /// - синхронизирует подключения и отключения с SwitchboardController.
    /// </summary>
    public sealed class SwitchboardInteractor : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SwitchboardController switchboardController;
        [SerializeField] private Camera interactionCamera;

        [Header("Настройки рейкаста")]
        [SerializeField] private LayerMask interactionMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;

        [Header("Следование штекера за курсором")]
        [Tooltip("Отступ плоскости перемещения штекера перед панелью коммутатора (в метрах).")]
        [SerializeField, Min(0.02f)] private float carryPlaneOutwardOffset = 0.095f;
        [SerializeField] private Vector2 carryClampX = new Vector2(0.16f, 0.80f);
        [SerializeField] private Vector2 carryClampY = new Vector2(0.74f, 1.05f);

        [Header("Порты и доки (заполняются автоматически)")]
        [SerializeField] private List<SwitchboardPort> ports = new List<SwitchboardPort>();
        [SerializeField] private List<SwitchboardDock> docks = new List<SwitchboardDock>();

        private readonly RaycastHit[] _raycastHits = new RaycastHit[24];
        private SwitchboardPort _hoveredPort;
        private SwitchboardDock _hoveredDock;

        public SwitchboardPlug ActivePlug { get; private set; }

        /// <summary>Срабатывает при физическом подключении штекера в порт (передаёт portId 1..10).</summary>
        public event Action<int> OnPortConnected;

        /// <summary>Срабатывает при физическом выдёргивании штекера из порта (передаёт portId 1..10).</summary>
        public event Action<int> OnPortDisconnected;

        private void Awake()
        {
            if (switchboardController == null)
            {
                switchboardController = GetComponent<SwitchboardController>();
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            CollectTargetsIfNeeded();
        }

        private void OnDisable()
        {
            ClearHoverHighlight();
        }

        private void Update()
        {
            if (GameplayInputGate.IsBlocked)
            {
                ClearHoverHighlight();
                return;
            }

            UpdateHoverAndCarryMovement();

            if (WasRightMousePressed())
            {
                ReturnActivePlugHome();
                return;
            }

            if (!WasLeftMousePressed(out Vector2 mousePosition))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            HandleLeftClick(mousePosition);
        }

        private void HandleLeftClick(Vector2 mousePosition)
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            Ray ray = cam.ScreenPointToRay(mousePosition);

            // Если штекер уже в руке — приоритетно проверяем порт и док (игнорируя коллайдер самого взятого штекера).
            if (ActivePlug != null)
            {
                if (TryRaycastTargetIgnoringActivePlug(ray, out SwitchboardPort hitPort, out SwitchboardDock hitDock, out SwitchboardPlug hitOtherPlug))
                {
                    if (hitPort != null && !hitPort.IsOccupied)
                    {
                        HandlePortClicked(hitPort);
                        return;
                    }

                    if (hitDock != null && hitDock.CanAcceptPlug(ActivePlug))
                    {
                        ReturnActivePlugHome();
                        return;
                    }

                    if (hitOtherPlug != null && hitOtherPlug != ActivePlug)
                    {
                        HandlePlugClicked(hitOtherPlug);
                        return;
                    }
                }

                return;
            }

            // Если рука пустая — ищем клик по любому штекеру.
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                SwitchboardPlug clickedPlug = hit.collider.GetComponentInParent<SwitchboardPlug>();
                if (clickedPlug != null)
                {
                    HandlePlugClicked(clickedPlug);
                }
            }
        }

        private void HandlePlugClicked(SwitchboardPlug clickedPlug)
        {
            if (ActivePlug == clickedPlug)
            {
                ReturnActivePlugHome();
                return;
            }

            if (ActivePlug != null)
            {
                ActivePlug.ReturnHome();
                ActivePlug = null;
            }

            if (clickedPlug.CurrentPort != null)
            {
                int disconnectedPortId = clickedPlug.CurrentPort.PortId;
                clickedPlug.PickUp();
                switchboardController?.OnPlugDisconnected(disconnectedPortId);
                OnPortDisconnected?.Invoke(disconnectedPortId);
            }
            else
            {
                clickedPlug.PickUp();
            }

            ActivePlug = clickedPlug;
            ClearHoverHighlight();
        }

        private void HandlePortClicked(SwitchboardPort clickedPort)
        {
            if (clickedPort.IsOccupied || ActivePlug == null)
            {
                return;
            }

            SwitchboardPlug plugToInsert = ActivePlug;
            ActivePlug = null;

            ClearHoverHighlight();
            plugToInsert.MoveToPort(clickedPort);
            switchboardController?.OnPlugConnected(clickedPort.PortId);
            OnPortConnected?.Invoke(clickedPort.PortId);
        }

        private void ReturnActivePlugHome()
        {
            if (ActivePlug == null)
            {
                return;
            }

            ActivePlug.ReturnHome();
            ActivePlug = null;
            ClearHoverHighlight();
        }

        private void UpdateHoverAndCarryMovement()
        {
            if (ActivePlug == null)
            {
                if (_hoveredPort != null || _hoveredDock != null)
                {
                    ClearHoverHighlight();
                }

                return;
            }

            Camera cam = ResolveCamera();
            if (cam == null || !TryGetMousePosition(out Vector2 mousePos))
            {
                return;
            }

            Ray ray = cam.ScreenPointToRay(mousePos);

            SwitchboardPort nextPort = null;
            SwitchboardDock nextDock = null;

            if (TryRaycastTargetIgnoringActivePlug(ray, out SwitchboardPort hitPort, out SwitchboardDock hitDock, out _))
            {
                if (hitPort != null && !hitPort.IsOccupied)
                {
                    nextPort = hitPort;
                }
                else if (hitDock != null && hitDock.CanAcceptPlug(ActivePlug))
                {
                    nextDock = hitDock;
                }
            }

            // Обновляем обводку ТОЛЬКО на том месте, на которое сейчас наведён курсор.
            if (nextPort != _hoveredPort)
            {
                if (_hoveredPort != null) _hoveredPort.SetHighlight(false);
                _hoveredPort = nextPort;
                if (_hoveredPort != null) _hoveredPort.SetHighlight(true);
            }

            if (nextDock != _hoveredDock)
            {
                if (_hoveredDock != null) _hoveredDock.SetHighlight(false);
                _hoveredDock = nextDock;
                if (_hoveredDock != null) _hoveredDock.SetHighlight(true);
            }

            // Двигаем взятый штекер за курсором (с аккуратным прицеливанием при наведении на порт или док).
            if (_hoveredPort != null)
            {
                ActivePlug.SetCarryTarget(_hoveredPort.GetHoverPreviewPosition(), _hoveredPort.GetPlugTargetRotation());
            }
            else if (_hoveredDock != null)
            {
                ActivePlug.SetCarryTarget(_hoveredDock.GetHoverPreviewPosition(), ActivePlug.HomeRotation);
            }
            else
            {
                MovePlugAlongCarryPlane(ray);
            }
        }

        private void MovePlugAlongCarryPlane(Ray ray)
        {
            Vector3 panelCenter;
            Vector3 panelInward;

            if (TryGetPanelReference(out panelCenter, out panelInward))
            {
                Vector3 outward = -panelInward;
                Plane carryPlane = new Plane(outward, panelCenter + outward * carryPlaneOutwardOffset);

                if (carryPlane.Raycast(ray, out float enter) && enter > 0.05f && enter < maxRayDistance)
                {
                    Vector3 worldPoint = ray.GetPoint(enter);
                    worldPoint.x = Mathf.Clamp(worldPoint.x, carryClampX.x, carryClampX.y);
                    worldPoint.y = Mathf.Clamp(worldPoint.y, carryClampY.x, carryClampY.y);

                    Quaternion aimRot = Quaternion.LookRotation(panelInward, Vector3.up);
                    ActivePlug.SetCarryTarget(worldPoint, aimRot);
                }
            }
        }

        private bool TryGetPanelReference(out Vector3 center, out Vector3 inward)
        {
            CollectTargetsIfNeeded();
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null)
                {
                    center = ports[i].transform.position;
                    inward = ports[i].GetInwardWorldDirection();
                    return true;
                }
            }

            center = transform.position;
            inward = -transform.forward;
            return true;
        }

        private bool TryRaycastTargetIgnoringActivePlug(
            Ray ray,
            out SwitchboardPort port,
            out SwitchboardDock dock,
            out SwitchboardPlug otherPlug)
        {
            port = null;
            dock = null;
            otherPlug = null;

            int hitCount = Physics.RaycastNonAlloc(ray, _raycastHits, maxRayDistance, interactionMask, QueryTriggerInteraction.Collide);
            if (hitCount <= 0)
            {
                return false;
            }

            // Сортируем попадания по дистанции (обычно 1-4 элемента).
            for (int i = 1; i < hitCount; i++)
            {
                RaycastHit current = _raycastHits[i];
                int j = i - 1;
                while (j >= 0 && _raycastHits[j].distance > current.distance)
                {
                    _raycastHits[j + 1] = _raycastHits[j];
                    j--;
                }
                _raycastHits[j + 1] = current;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _raycastHits[i].collider;
                if (col == null)
                {
                    continue;
                }

                SwitchboardPlug hitPlug = col.GetComponentInParent<SwitchboardPlug>();
                if (hitPlug != null)
                {
                    if (hitPlug == ActivePlug)
                    {
                        continue; // Пропускаем штекер, который прямо сейчас под курсором в руке.
                    }

                    otherPlug = hitPlug;
                    return true;
                }

                SwitchboardPort hitPort = col.GetComponentInParent<SwitchboardPort>();
                if (hitPort != null)
                {
                    port = hitPort;
                    return true;
                }

                SwitchboardDock hitDock = col.GetComponentInParent<SwitchboardDock>();
                if (hitDock != null)
                {
                    dock = hitDock;
                    return true;
                }
            }

            return false;
        }

        private void ClearHoverHighlight()
        {
            if (_hoveredPort != null)
            {
                _hoveredPort.SetHighlight(false);
                _hoveredPort = null;
            }

            if (_hoveredDock != null)
            {
                _hoveredDock.SetHighlight(false);
                _hoveredDock = null;
            }
        }

        private void CollectTargetsIfNeeded()
        {
            if (ports.Count == 0)
            {
                GetComponentsInChildren(true, ports);
            }

            if (docks.Count == 0)
            {
                GetComponentsInChildren(true, docks);
            }
        }

        private Camera ResolveCamera()
        {
            if (interactionCamera != null)
            {
                return interactionCamera;
            }

            interactionCamera = Camera.main;
            return interactionCamera;
        }

        private static bool TryGetMousePosition(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            mousePosition = Input.mousePosition;
            return true;
#endif
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
    }
}
