using System;

public class Utils
{
	public static int GetAngle( Vector2 vector )
	{
		return (int)(
			360 + Math.Round(
				Math.Atan2( vector.y, vector.x ) * 180 / Math.PI
			) % 360
		);
	}
}
