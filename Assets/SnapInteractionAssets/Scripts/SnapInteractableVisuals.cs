using Oculus.Interaction;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

public class SnapInteractableVisuals : MonoBehaviour
{
    [SerializeField] private SnapInteractable snapInteractable;
    [SerializeField] private Material hoverMaterial;

    private GameObject currentInteractorGameObject;
    private SnapInteractor currentInteractor;

    private void OnEnable()
    {
        snapInteractable.WhenInteractorAdded.Action += WhenInteractorAdded_Action;
        snapInteractable.WhenSelectingInteractorViewAdded += SnapInteractable_WhenSelectingInteractorViewAdded;
        snapInteractable.WhenInteractorViewRemoved += SnapInteractable_WhenInteractorViewRemoved;
        snapInteractable.WhenInteractorViewAdded += SnapInteractable_WhenInteractorViewAdded;
    }

    private void WhenInteractorAdded_Action(SnapInteractor obj)
    {
        if (currentInteractor == null)
            currentInteractor = obj;
        else if (currentInteractor != obj)
        {
            currentInteractor = obj;
            var tempGP = currentInteractorGameObject;
            Destroy(tempGP);
            currentInteractorGameObject = null;
        }
        else
            return;

        SetupGhostModel(obj);
    }

    private void SnapInteractable_WhenSelectingInteractorViewAdded(IInteractorView obj)
    {
        currentInteractorGameObject?.SetActive(false);
    }

    private void SnapInteractable_WhenInteractorViewAdded(IInteractorView obj)
    {
        currentInteractorGameObject?.SetActive(true);
    }

    private void SnapInteractable_WhenInteractorViewRemoved(IInteractorView obj)
    {
        currentInteractorGameObject.SetActive(false);
    }

    private void SetupGhostModel(SnapInteractor interactor)
    {
        currentInteractorGameObject = new GameObject(interactor.transform.parent?.name);
        currentInteractorGameObject.transform.parent = transform;
        currentInteractorGameObject.transform.localScale = interactor.transform.parent.localScale;
        currentInteractorGameObject.transform.localPosition = Vector3.zero;
        currentInteractorGameObject.transform.localRotation = Quaternion.identity;

        var parentMesh = interactor.transform.parent.GetComponent<MeshFilter>();
        if (parentMesh != null)
        {
            currentInteractorGameObject.AddComponent<MeshFilter>().mesh = parentMesh.mesh;
            currentInteractorGameObject.AddComponent<MeshRenderer>().material = hoverMaterial;
        }

        var childMesh = interactor.transform.parent.GetComponentsInChildren<MeshFilter>();
        if (childMesh != null)
        {
            foreach (var item in childMesh)
            {
                var newGo = new GameObject(item.name);
                newGo.transform.parent = currentInteractorGameObject.transform;
                newGo.transform.localPosition = item.transform.localPosition;
                newGo.transform.localRotation = item.transform.localRotation;
                newGo.transform.localScale = item.transform.localScale;
                newGo.AddComponent<MeshFilter>().mesh = item.mesh;
                newGo.AddComponent<MeshRenderer>().material = hoverMaterial;
            }
        }
    }
}
