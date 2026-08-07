using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class earthquicksystem : MonoBehaviour
{
    //======================== References ========================//

    // Large objects like buildings, rocks, bridges etc.
    public List<GameObject> big_object;

    // Smaller movable objects like crates, barrels, chairs etc.
    public List<GameObject> movable_obj;

    // Camera that will shake
    public Camera maincam;


    //===================== Earthquake Settings =====================//

    [Header("Earthquake Settings")]

    // Total duration of the earthquake
    public float duration = 5f;

    // Camera shake intensity
    public float cameraShakeAmount = 0.25f;

    // Shake amount for large objects
    public float bigObjectShakeAmount = 0.03f;

    // Shake amount for movable objects
    public float movableObjectShakeAmount = 0.08f;

    // (Used later if you want smooth Perlin shake)
    public float speed = 25f;


    //======================= Private Variables =======================//

    // Original camera position before shaking
    private Vector3 cameraOriginalPos;

    // Stores original positions of all big objects
    private Dictionary<Transform, Vector3> bigOriginalPos =
        new Dictionary<Transform, Vector3>();

    // Stores original positions of all movable objects
    private Dictionary<Transform, Vector3> movableOriginalPos =
        new Dictionary<Transform, Vector3>();

    // Prevents the earthquake from starting twice
    private bool hasStarted = false;



    private void Start()
    {
        //----------------------------------------------------------
        // Save the camera's starting position.
        // We'll reset to this once the earthquake ends.
        //----------------------------------------------------------

        cameraOriginalPos = maincam.transform.localPosition;


        //----------------------------------------------------------
        // Save every big object's original position.
        //----------------------------------------------------------

        foreach (GameObject obj in big_object)
        {
            if (obj != null)
                bigOriginalPos.Add(obj.transform, obj.transform.localPosition);
        }


        //----------------------------------------------------------
        // Save every movable object's original position.
        //----------------------------------------------------------

        foreach (GameObject obj in movable_obj)
        {
            if (obj != null)
                movableOriginalPos.Add(obj.transform, obj.transform.localPosition);
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        // Don't allow the earthquake to start again.
        if (hasStarted)
            return;

        // Only trigger if the player enters.
        if (other.CompareTag("Player"))
        {
            hasStarted = true;

            // Disable player movement.
            scripts_disable_container.sdc.player_all_script_changer(false);

            // Start the earthquake.
            StartCoroutine(StartShaking());
        }
    }



    IEnumerator StartShaking()
    {
        float timer = 0f;

        //----------------------------------------------------------
        // Keep shaking until the timer reaches the duration.
        //----------------------------------------------------------

        while (timer < duration)
        {
            timer += Time.deltaTime;


            //======================================================
            // CAMERA SHAKE
            //======================================================

            maincam.transform.localPosition =
                cameraOriginalPos +
                Random.insideUnitSphere * cameraShakeAmount;



            //======================================================
            // SHAKE ALL BIG OBJECTS
            //======================================================

            foreach (var obj in bigOriginalPos)
            {
                obj.Key.localPosition =
                    obj.Value +
                    Random.insideUnitSphere * bigObjectShakeAmount;
            }



            //======================================================
            // SHAKE ALL SMALL OBJECTS
            //======================================================

            foreach (var obj in movableOriginalPos)
            {
                obj.Key.localPosition =
                    obj.Value +
                    Random.insideUnitSphere * movableObjectShakeAmount;
            }


            // Wait until the next frame.
            yield return null;
        }



        //----------------------------------------------------------
        // Earthquake finished.
        // Reset everything back to its original position.
        //----------------------------------------------------------

        maincam.transform.localPosition = cameraOriginalPos;


        foreach (var obj in bigOriginalPos)
            obj.Key.localPosition = obj.Value;


        foreach (var obj in movableOriginalPos)
            obj.Key.localPosition = obj.Value;


        // Give player control back.
        scripts_disable_container.sdc.player_all_script_changer(true);
    }
}