using UnityEngine;

namespace DeliveryRider
{
    // Anything the rider's punch can affect: recipients, thieves and, later, passers-by and props.
    public interface IPunchable
    {
        // Returns true when the punch changed something (delivered, recovered food, knocked over).
        bool ReceivePunch(Vector3 direction);
    }
}
