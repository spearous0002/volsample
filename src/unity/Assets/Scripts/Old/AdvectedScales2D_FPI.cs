/*
The MIT License (MIT)

Copyright (c) 2015 Huw Bowles & Daniel Zimmermann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// this is an attempt to generalise the sample slice advection based on FPI to support full 3D transformations
// (so a 2D ray scale texture). the core works but the slice extension proved to be difficult and was never
// implemented

using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
public class AdvectedScales2D_FPI : MonoBehaviour
{
	[Tooltip("Used to differentiate/sort the two advection computations")]
	public int radiusIndex = 0;

	[Tooltip("The radius of this sample slice. Advection is performed at this radius")]
	public float radius = 10.0f;

	AdvectedScalesSettings settings;

	public float[,] scales_norm;

	Vector3 lastPos;
	Vector3 lastForward;
	Vector3 lastRight;
	Vector3 lastUp;

	float motionMeasure = 0.0f;

	void Start()
	{
		settings = GetComponent<AdvectedScalesSettings> ();

		scales_norm = new float[settings.scaleCount,settings.scaleCount];

		// init scales to something interesting
		for( int j = 0; j < settings.scaleCount; j++ )
		{
			float phi = getPhi( j );
			for( int i = 0; i < settings.scaleCount; i++ )
			{
				float theta = getTheta( i );

				float fixedZ = Mathf.Lerp( Mathf.Cos(CloudsBase.halfFov_horiz_rad), 1.0f, settings.fixedZProp ) / (Mathf.Sin(theta)*Mathf.Cos(phi));
				float fixedR = (Mathf.Sin(13f*j/(float)settings.scaleCount)*Mathf.Sin(8f*i/(float)settings.scaleCount)*settings.fixedRNoise + 1f);
				scales_norm[i,j] = Mathf.Lerp( fixedZ, fixedR, settings.reInitCurvature );
			}
		}

		lastPos = transform.position;
		lastForward = transform.forward;
		lastRight = transform.right;
		lastUp = transform.up;
	}

	void Update()
	{
		if( scales_norm == null || settings.scaleCount*settings.scaleCount != scales_norm.Length || settings.reInitScales )
			Start();

		float dt = Mathf.Max( Time.deltaTime, 0.033f );

		float vel = (transform.position - lastPos).magnitude / dt;
		if( settings.clearOnTeleport && vel > 100.0f )
		{
			ClearAfterTeleport();
			return;
		}

		// compute motion measures
		// compute rotation of camera (heading only)
		float rotRad = Vector3.Angle( transform.forward, lastForward ) * Mathf.Sign( Vector3.Cross( lastForward, transform.forward ).y ) * Mathf.Deg2Rad;

		// compute motion measure
		float motionMeasureRot = Mathf.Clamp01(Mathf.Abs(settings.motionMeasCoeffRot * rotRad/dt));
		float motionMeasureTrans = Mathf.Clamp01(Mathf.Abs(settings.motionMeasCoeffStrafe * vel));
		motionMeasure = Mathf.Max( motionMeasureRot, motionMeasureTrans );
		if (!settings.useMotionMeas)
			motionMeasure = 1;

		// working data
		float[,] scales_new_norm = new float[settings.scaleCount, settings.scaleCount];


		////////////////////////////////
		// advection

		if( settings.doAdvection )
		{
			bool oneIn = false;

			for( int j = 0; j < settings.scaleCount; j++ )
			{
				float phi1 = getPhi(j);

				for( int i = 0; i < settings.scaleCount; i++ )
				{
					float theta1 = getTheta(i);

					float theta0, phi0;
					InvertAdvection( theta1, phi1, out theta0, out phi0 );

					float r1 = ComputeR1( theta0, phi0 );

					scales_new_norm[i, j] = r1 / radius;

					// detect case where no samples were taken from the old slice
					oneIn = oneIn || (thetaWithinView(theta0) && phiWithinView(phi0));
				}
			}

			/*
			// if no samples taken, call this a teleport, reinit scales
			if( !oneIn && !settings.debugFreezeAdvection )
			{
				ClearAfterTeleport();
			}
			*/


			/*
			// now clamp the scales
			if( settings.clampScaleValues )
			{
				// clamp the values
				for( int j = 0; j < settings.scaleCount; j++ )
				{
					for( int i = 0; i < settings.scaleCount; i++ )
					{
						// min: an inverted circle, i.e. concave instead of convex. this allows obiting
						// max: the fixed z line at the highest point of the circle. this allows strafing after rotating without aliasing
						scales_new_norm[i,j] = Mathf.Clamp( scales_new_norm[i,j], 0.9f*(1.0f - (Mathf.Sin(getTheta(i))-Mathf.Cos(CloudsBase.halfFov_horiz_rad))), 1.0f/Mathf.Sin(getTheta(i)) );
					}
				}
			}
			*/


			/*
			// limit/relax the gradients
			if( settings.limitGradient )
			{
				RelaxGradients( scales_new_norm );
			}
			*/



			// all done - store the new r values
			if( !settings.debugFreezeAdvection )
			{
				// normal path - store the scales then debug draw

				for( int j = 0; j < settings.scaleCount; j++ )
				{
					for( int i = 0; i < settings.scaleCount; i++ )
					{
						scales_norm[i, j] = scales_new_norm[i, j];
					}
				}

				Draw();
			}
			/*else
			{
				// for freezing advection, apply the scales temporarily and draw them, but then revert them

				float[] bkp = new float[scales_norm.Length];
				for( int i = 0; i < bkp.Length; i++ )
					bkp[i] = scales_norm[i];

				for( int i = 0; i < settings.scaleCount; i++ )
					scales_norm[i] = scales_new_norm[i];

				Draw();

				for( int i = 0; i < settings.scaleCount; i++ )
					scales_norm[i] = bkp[i];
			}*/
		}

		if( !settings.debugFreezeAdvection )
		{
			lastPos = transform.position;
			lastForward = transform.forward;
			lastRight = transform.right;
			lastUp = transform.up;
		}
	}


	// gradient relaxation
	// the following is complex and I don't know how much of this could maybe be done
	// in a single pass or otherwise simplified. more experimentation is needed.
	//
	// the two scales at the sides of the frustum are not changed by this process.
	// there are two stages - the first is an inside-out scheme which starts from
	// the middle and moves outwards. the second is an outside-in scheme which moves
	// towards the middle from the side.
	void RelaxGradients( float[] r_new )
	{
		// INSIDE OUT

		for( int i = settings.scaleCount/2; i < settings.scaleCount-1; i++ )
		{
			RelaxGradient( i, i-1, r_new );
		}

		for( int i = settings.scaleCount/2; i >= 1; i-- )
		{
			RelaxGradient( i, i+1, r_new );
		}

		// OUTSIDE IN

		for( int i = 1; i <= settings.scaleCount/2; i++ )
		{
			RelaxGradient( i, i-1, r_new );
		}

		for( int i = settings.scaleCount-2; i >= settings.scaleCount/2; i-- )
		{
			RelaxGradient( i, i+1, r_new );
		}
	}

	void RelaxGradient( int i, int i1, float[] scales_new_norm )
	{
		float dx = radius*scales_new_norm[i]*Mathf.Cos(getTheta(i)) - radius*scales_new_norm[i1]*Mathf.Cos(getTheta(i1));
		if( Mathf.Abs(dx) < 0.0001f )
		{
			//Debug.LogError("dx too small! " + dx);
			dx = Mathf.Sign(dx) * 0.0001f;
		}

		float meas = ( radius*scales_new_norm[i]*Mathf.Sin(getTheta(i)) - radius*scales_new_norm[i1]*Mathf.Sin(getTheta(i1)) ) / dx;
		float measClamped = Mathf.Clamp( meas, -settings.maxGradient, settings.maxGradient );

		float dt = Mathf.Max( Time.deltaTime, 1.0f/30.0f );
		scales_new_norm[i] = Mathf.Lerp( radius*scales_new_norm[i], (measClamped*dx + radius*scales_new_norm[i1]*Mathf.Sin(getTheta(i1))) / Mathf.Sin(getTheta(i)), motionMeasure*settings.alphaGradient * 30.0f * dt ) / radius;
	}

	// reset scales to a fixed-z layout
	void ClearAfterTeleport()
	{
		Debug.Log( "Teleport event, resetting layout" );
		
		for( int j = 0; j < settings.scaleCount; j++ )
		{
			float cos_phi = Mathf.Cos( getPhi( j ) );
			for( int i = 0; i < settings.scaleCount; i++ )
			{
				scales_norm[i, j] = Mathf.Cos( CloudsBase.halfFov_horiz_rad ) / (Mathf.Sin(getTheta(i))*cos_phi);
			}
		}
		
		lastPos = transform.position;
		lastForward = transform.forward;
		lastRight = transform.right;
		lastUp = transform.up;
	}

	public float getTheta( int i ) { return CloudsBase.halfFov_horiz_rad * (2.0f * (float)i/(float)(settings.scaleCount-1) - 1.0f) + Mathf.PI/2.0f; }
	bool thetaWithinView( float theta ) { return Mathf.Abs( theta - Mathf.PI/2.0f ) <= CloudsBase.halfFov_horiz_rad; }
	public float getPhi( int j ) { return CloudsBase.halfFov_vert_rad * (2.0f * (float)j/(float)(settings.scaleCount-1) - 1.0f); }
	bool phiWithinView( float phi ) { return Mathf.Abs( phi ) <= CloudsBase.halfFov_vert_rad; }
	
	Vector3 GetRay( float theta )
	{
		Quaternion q = Quaternion.AngleAxis( theta * Mathf.Rad2Deg, -Vector3.up );
		return q * transform.right;
	}

	// note that theta for the center of the screen is PI/2. its really incovenient that angles are computed from the X axis, but
	// the view direction is down the Z axis. we adopted this scheme as it made a bunch of the math simpler (i think!)
	//
	//          Z axis (theta = PI/2)
	//            |
	// frust. \   |   /
	//         \  |  /
	//          \ | /   \ theta
	//           \|/     |
	// ----------------------- X axis (theta = 0)
	//
	// for theta within the view, we sample the scale from the sample slice directly
	// for theta outside the view, we compute a linear extension from the last point on
	// the sample slice, to the corresponding scale at the side of the new camera position
	// frustum. this is a linear approximation to how the sample slice needs to be extended
	// when the frustum moves. if you rotate the camera fast then you will see the linear
	// segments.
	public float sampleR( float theta, float phi )
	{
		// move theta from [pi/2 - halfFov, pi/2 + halfFov] to [0,1]
		float s = (theta - (Mathf.PI/2.0f-CloudsBase.halfFov_horiz_rad))/(2.0f*CloudsBase.halfFov_horiz_rad);
		float t = (phi + CloudsBase.halfFov_vert_rad)/(2.0f*CloudsBase.halfFov_vert_rad);
		
		s = Mathf.Clamp01(s);
		t = Mathf.Clamp01(t);
		/*if( s < 0f || s > 1f )
		{
			// determine which side we're on. s<0 is right side as angles increase anti-clockwise
			bool rightSide = s < 0f;
			int lastIndex = rightSide ? 0 : settings.scaleCount-1;

			// the start and end position of the extension
			Vector3 pos_slice_end, pos_extrapolated;

			pos_slice_end = lastPos
				+ radius * scales_norm[lastIndex] * Mathf.Cos( getTheta(lastIndex) ) * lastRight
				+ radius * scales_norm[lastIndex] * Mathf.Sin( getTheta(lastIndex) ) * lastForward;

			float theta_edge = getTheta(lastIndex);

			// we always nudge scale back to default val (scale return). to compute how much we're nudging
			// the scale, we find our how far we're extending it, and we do this in radians.
			Vector3 extrapolatedDir = transform.forward * Mathf.Sin(theta_edge) + transform.right * Mathf.Cos(theta_edge);
			float angleSubtended = Vector3.Angle( pos_slice_end - transform.position, extrapolatedDir ) * Mathf.Deg2Rad;
			float lerpAlpha = Mathf.Clamp01( motionMeasure*settings.alphaScaleReturn*angleSubtended );
			float r_extrap = Mathf.Lerp( sampleR(theta_edge), radius, lerpAlpha );

			// now compute actual pos
			pos_extrapolated = transform.position
				+ transform.forward * r_extrap * Mathf.Sin(theta_edge)
				+ transform.right * r_extrap * Mathf.Cos(theta_edge);

			// now intersect ray with extension to find scale.
			Vector3 rayExtent = lastPos
				+ Mathf.Cos( theta ) * lastRight
				+ Mathf.Sin( theta ) * lastForward;

			Vector2 inter; bool found;
			found = IntersectLineSegments(
				new Vector2( pos_slice_end.x, pos_slice_end.z ),
				new Vector2( pos_extrapolated.x, pos_extrapolated.z ),
				new Vector2( lastPos.x, lastPos.z ),
				new Vector2( rayExtent.x, rayExtent.z ),
				out inter
				);

			// no unique intersection point - shouldnt happen
			if( !found )
				return sampleR(theta_edge);

			// the intersection point between the ray for the query theta and the linear extension
			Vector3 pt = new Vector3( inter.x, 0f, inter.y );

			// make flatland
			Vector3 offset = pt - lastPos;
			offset.y = 0f;

			return offset.magnitude;
		}*/

		// get from 0 to rCount-1
		s *= (float)(settings.scaleCount-1);
		t *= (float)(settings.scaleCount-1);
		
		int i0 = Mathf.FloorToInt(s);
		int i1 = Mathf.CeilToInt(s);
		int j0 = Mathf.FloorToInt(t);
		int j1 = Mathf.CeilToInt(t);

		float resultj0 = Mathf.Lerp( scales_norm[i0,j0], scales_norm[i1,j0], Mathf.Repeat(s, 1.0f) );
		float resultj1 = Mathf.Lerp( scales_norm[i0,j1], scales_norm[i1,j1], Mathf.Repeat(s, 1.0f) );

		float result = Mathf.Lerp( resultj0, resultj1, Mathf.Repeat(t, 1.0f) );

		return radius * result;
	}


	// this assumes that sampleR, lastPos, etc all return values from the PREVIOUS frame!
	Vector3 ComputePos0_world( float theta, float phi )
	{
		float r0 = sampleR( theta, phi );

		// 3d polar coordinates
		return lastPos
			+ r0 * Mathf.Cos(phi) * (Mathf.Cos(theta) * lastRight + Mathf.Sin(theta) * lastForward)
			+ r0 * Mathf.Sin(phi) * lastUp;
	}


	// solver. after the camera has moved, for a particular ray scale at angle theta1, we can find the angle to the corresponding
	// sample before the camera move theta0 using an iterative computation - fixed point iteration.
	// we previously published a paper titled Iterative Image Warping about using FPI for very similar use cases:
	// http://www.disneyresearch.com/wp-content/uploads/Iterative-Image-Warping-Paper.pdf
	void InvertAdvection( float theta1, float phi1, out float theta0, out float phi0 )
	{
		// just guess the source position is the current pos (basically, that the camera hasn't moved)
		theta0 = theta1;
		phi0 = phi1;
		
		// N iterations of FPI. compute where our guess would get us, and then update our guess with the error.
		// we could monitor the iteration to ensure convergence etc but the advection seems to be well behaved for 8 iterations
		for( int i = 0; i < settings.advectionIters; i++ )
		{
			float newTheta0 = theta0 + theta1 - ComputeTheta1( theta0, phi0 );
			float newPhi0 = phi0 + phi1 - ComputePhi1( theta0, phi0 );
			theta0 = newTheta0;
			phi0 = newPhi0;
		}
	}

	// input: theta0 is angle before camera moved, which specifies a specific point on the sample slice P
	// output: theta1 gives angle after camera moved to the sample slice point P
	// this is the opposite to what we want - we will know the angle afterwards theta1 and want to compute
	// the angle before theta0. however we invert this using FPI.
	float ComputeTheta1( float theta0, float phi0 )
	{
		Vector3 pos0 = ComputePos0_world( theta0, phi0 );
		
		// end position, removing foward motion of cam, in local space
		Vector3 pos1_local = transform.InverseTransformPoint( pos0 );
		
		float pullInCam = Vector3.Dot(transform.position-lastPos, transform.forward);
		if( !settings.advectionCompensatesForwardPin )
			pos1_local += pullInCam * Vector3.forward;
		else
			pos1_local += pullInCam * pos1_local.normalized * sampleR(theta0, phi0) / sampleR(Mathf.PI/2.0f, 0.0f);
		
		return Mathf.Atan2( pos1_local.z, pos1_local.x );
	}
	float ComputePhi1( float theta0, float phi0 )
	{
		Vector3 pos0 = ComputePos0_world( theta0, phi0 );
		
		// end position, removing foward motion of cam, in local space
		Vector3 pos1_local = transform.InverseTransformPoint( pos0 );
		
		float pullInCam = Vector3.Dot(transform.position-lastPos, transform.forward);
		if( !settings.advectionCompensatesForwardPin )
			pos1_local += pullInCam * Vector3.forward;
		else
			pos1_local += pullInCam * pos1_local.normalized * sampleR(theta0, phi0) / sampleR(Mathf.PI/2.0f, 0.0f);

		// 3d polar coordinates are fun
		return Mathf.Atan2( pos1_local.y, Mathf.Sqrt(pos1_local.x*pos1_local.x+pos1_local.z*pos1_local.z) );
	}


	// for an angle theta0 specifying a point on the sample slice before the camera moves,
	// we can compute a radius to this point by computing the distance to the new camera
	// position. this completes the advection computation
	float ComputeR1( float theta0, float phi0 )
	{
		Vector3 pos0 = ComputePos0_world( theta0, phi0 );
		
		// end position, removing forward motion of cam
		Vector3 pos1 = transform.position;
		
		float pullInCam = Vector3.Dot(transform.position-lastPos, transform.forward);
		
		if( !settings.advectionCompensatesForwardPin )
			pos1 -= pullInCam * transform.forward;
		
		Vector3 offset = pos0 - pos1;
		
		if( settings.advectionCompensatesForwardPin )
			offset += pullInCam * offset.normalized * sampleR(theta0, phi0) / sampleR(Mathf.PI/2f, 0f);
		
		return offset.magnitude;
	}

	void Draw()
	{
		float angleExpand = 1.2f;

		for( int j = 1; j < settings.scaleCount; j++ )
		{
			float prevPhi = getPhi(j-1) * angleExpand;
			float thisPhi = getPhi(j) * angleExpand;

			for( int i = 1; i < settings.scaleCount; i++ )
			{
				// only draw every 4th for perf reasons
				if( ( i + j ) % 4 != 0 )
					continue;

				float prevTheta = getTheta(i-1);
				float thisTheta = getTheta(i);
				prevTheta = Mathf.PI/2.0f + (prevTheta-Mathf.PI/2.0f) * angleExpand;
				thisTheta = Mathf.PI/2.0f + (thisTheta-Mathf.PI/2.0f) * angleExpand;

				Color col = Color.white;
				if( !thetaWithinView(thisTheta) || !thetaWithinView(prevTheta) || !phiWithinView(thisPhi) || !phiWithinView(prevPhi) )
					col *= 0.5f;

				Debug.DrawLine( ComputePos0_world( prevTheta, prevPhi ), ComputePos0_world( thisTheta, thisPhi ), col );
				
				/*if( settings.debugDrawAdvectionGuides )
				{
					scale = settings.debugDrawScale * 1.0f;
					Color fadeRed = Color.red;
					fadeRed.a = 0.25f;
					Debug.DrawLine( transform.position + prevRd * radius * scale, transform.position + thisRd * radius * scale, fadeRed );
					scale = Mathf.Cos(CloudsBase.halfFov_horiz_rad) / Mathf.Sin(thisTheta);
					Debug.DrawLine( transform.position + prevRd * radius * scale, transform.position + thisRd * radius * scale, fadeRed );
				}*/
			}
		}
	}

	// loosely based on http://ideone.com/PnPJgb
	// performance of this is very bad, lots of temp things being constructed. i should really just inline the code
	// above but leaving like this for now.
	static bool IntersectLineSegments(Vector2 A, Vector2 B, Vector2 C, Vector2 D, out Vector2 intersect )
	{
		Vector2 r = B - A;
		Vector2 s = D - C;
		
		float rxs = r.x * s.y - r.y * s.x;
		
		if( Mathf.Abs(rxs) <= Mathf.Epsilon )
		{
			// Lines are parallel or collinear - this is useless for us
			intersect = Vector2.zero;
			return false;
		}
		
		Vector2 CmA = C - A;
		float CmAxs = CmA.x * s.y - CmA.y * s.x;

		float t = CmAxs / rxs;

		intersect = Vector2.Lerp( A, B, t );

		return true;
	}
}