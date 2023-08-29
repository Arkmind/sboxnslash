using Sandbox;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SboxNSlash.Character;

public class CharacterController : EntityComponent<BasePawn>
{
	public int StepSize => 24;
	public int GroundAngle => 45;
	public int JumpSpeed => 410;
	public float Gravity => 800f;

	HashSet<string> ControllerEvents = new( StringComparer.OrdinalIgnoreCase );

	bool Grounded => Entity.GroundEntity.IsValid();
	int DoubleJump { get; set; }
	[ClientInput] Vector3 LastWorldClickPosition { get; set; }
	[ClientInput] DateTime LastWalkParticleEmitDate { get; set; }

	public void Simulate( IClient cl )
	{
		ControllerEvents.Clear();

		Vector3 Distance = LastWorldClickPosition - Entity.Position;
		Vector3 Direction = new Vector3( Distance.x, Distance.y, 0f ).Normal * 320f;

		var groundEntity = CheckForGround();

		if ( groundEntity.IsValid() )
		{
			if ( !Grounded )
			{
				Entity.Velocity = Entity.Velocity.WithZ( 0 );
				AddEvent( "grounded" );
			}

			DebugOverlay.Line( LastWorldClickPosition, Entity.Position, Color.White );

			if ( Entity.Position.Distance( LastWorldClickPosition ) > 100f )
			{
				Entity.Velocity = Accelerate( Entity.Velocity, Direction.Normal, Direction.Length, 200.0f * (Input.Down( "run" ) ? 2.5f : 1f), 7.5f );
				Entity.Velocity = ApplyFriction( Entity.Velocity, 4.0f );
			}
			else if ( Entity.Position.Distance( LastWorldClickPosition ) < 100f && Entity.Position.Distance( LastWorldClickPosition ) > 10f )
			{
				Entity.Velocity = Accelerate( Entity.Velocity, Direction.Normal, Direction.Length, 100.0f * (Input.Down( "run" ) ? 2.5f : 1f), 7.5f );
				Entity.Velocity = ApplyFriction( Entity.Velocity, 4.0f );
			}
			else
			{
				Entity.Velocity = Vector3.Zero;
				Entity.Velocity += Vector3.Down * Gravity * Time.Delta;
			}

			if ( Entity.Velocity.Length > 100f )
			{
				if ( LastWalkParticleEmitDate < DateTime.Now + TimeSpan.FromMilliseconds( 1000 - Entity.Velocity.Length ) )
				{
					GenerateWalkSprite();
					LastWalkParticleEmitDate = DateTime.Now;
				}
			}

			Entity.ViewAngles = new Angles( new Vector3( 0f, Entity.Position.Angle( LastWorldClickPosition ), 0f ) );
		}
		else
		{
			Entity.Velocity = Accelerate( Entity.Velocity, Direction.Normal, Direction.Length, 15, 20f );
			Entity.Velocity += Vector3.Down * Gravity * Time.Delta;
		}

		Entity.ViewAngles = new Angles( new Vector3(
			0f,
			Utils.GetAngle(
				new Vector2(
					LastWorldClickPosition.x - Entity.Position.x,
					LastWorldClickPosition.y - Entity.Position.y
				)
			),
			0f
		) );

		if ( Input.Down( "attack1" ) )
		{
			DoAttack1();
		}

		if ( Input.Pressed( "jump" ) )
		{
			SpellTest();
		}

		var mh = new MoveHelper( Entity.Position, Entity.Velocity );
		mh.Trace = mh.Trace.Size( Entity.Hull ).Ignore( Entity );

		if ( mh.TryMoveWithStep( Time.Delta, StepSize ) > 0 )
		{
			if ( Grounded )
			{
				mh.Position = StayOnGround( mh.Position );
			}
			Entity.Position = mh.Position;
			Entity.Velocity = mh.Velocity;
		}

		Entity.GroundEntity = groundEntity;
	}

	void GenerateWalkSprite()
	{
		Particles WalkParticle = Particles.Create( "particles/walk_particle.vpcf" );

		// Dynamic Particle by Velocity
		WalkParticle.Set( "Count", Entity.Velocity.Length / 184f );
		WalkParticle.Set( "Radius", Entity.Velocity.Length / 50f );
		WalkParticle.Set( "SpriteScale", Entity.Velocity.Length / 100f );

		WalkParticle.SetPosition( 0, Entity.Position );
		WalkParticle.Destroy( false );
	}

	void SpellTest()
	{
		Log.Info( "Spell Test" );

		Particles SpellParticle = Particles.Create( "particles/spell_test.vpcf" );

		SpellParticle.SetEntity( 0, Entity, true );

		SpellParticle.Destroy( false );
	}

	void DoJump()
	{
		if ( Grounded )
		{
			DoubleJump = 0;
			Entity.Velocity = ApplyJump( Entity.Velocity, "jump" );
		}
		else if ( DoubleJump == 0 )
		{
			Particles JumpParticle = Particles.Create( "particles/jump_particle.vpcf" );

			JumpParticle.SetPosition( 0, Entity.Position );

			JumpParticle.Destroy( false );

			DoubleJump = 1;
			Entity.Velocity = ApplyJump( Entity.Velocity, "doublejump" );
		}
	}

	void DoAttack1()
	{
		Vector3 targetPosition = MouseToWorldPosition();

		LastWorldClickPosition = targetPosition;
	}

	Vector3 MouseToWorldPosition()
	{
		Vector3 direction = Screen.GetDirection( Mouse.Position, Camera.FieldOfView, Camera.Rotation, Screen.Size );
		var trace = Trace.Ray( Camera.Position, Camera.Position + direction * 100000 )
			.UseHitboxes()
			.WithoutTags( "player" )
			.WithAnyTags( "solid" )
			.Run();

		if ( trace.Hit )
		{
			DebugOverlay.Sphere( trace.HitPosition, 10, Color.Red, 1 );
			return trace.HitPosition;
		}
		else
		{
			Log.Info( "Mouse Failed to Hit World Space" );
			return Vector3.Zero;
		}
	}

	Entity CheckForGround()
	{
		if ( Entity.Velocity.z > 300f )
			return null;

		var trace = Entity.TraceBBox( Entity.Position, Entity.Position + Vector3.Down, 2f );

		if ( !trace.Hit )
			return null;

		if ( trace.Normal.Angle( Vector3.Up ) > GroundAngle )
			return null;

		return trace.Entity;
	}

	Vector3 ApplyFriction( Vector3 input, float frictionAmount )
	{
		float StopSpeed = 100.0f;

		var speed = input.Length;
		if ( speed < 0.1f ) return input;

		// Bleed off some speed, but if we have less than the bleed
		// threshold, bleed the threshold amount.
		float control = (speed < StopSpeed) ? StopSpeed : speed;

		// Add the amount to the drop amount.
		var drop = control * Time.Delta * frictionAmount;

		// scale the velocity
		float newspeed = speed - drop;
		if ( newspeed < 0 ) newspeed = 0;
		if ( newspeed == speed ) return input;

		newspeed /= speed;
		input *= newspeed;

		return input;
	}

	Vector3 Accelerate( Vector3 input, Vector3 wishdir, float wishspeed, float speedLimit, float acceleration )
	{
		if ( speedLimit > 0 && wishspeed > speedLimit )
			wishspeed = speedLimit;

		var currentspeed = input.Dot( wishdir );
		var addspeed = wishspeed - currentspeed;

		if ( addspeed <= 0 )
			return input;

		var accelspeed = acceleration * Time.Delta * wishspeed;

		if ( accelspeed > addspeed )
			accelspeed = addspeed;

		input += wishdir * accelspeed;

		return input;
	}

	Vector3 ApplyJump( Vector3 input, string jumpType )
	{
		AddEvent( jumpType );

		return input + Vector3.Up * JumpSpeed;
	}

	Vector3 StayOnGround( Vector3 position )
	{
		var start = position + Vector3.Up * 2;
		var end = position + Vector3.Down * StepSize;

		// See how far up we can go without getting stuck
		var trace = Entity.TraceBBox( position, start );
		start = trace.EndPosition;

		// Now trace down from a known safe position
		trace = Entity.TraceBBox( start, end );

		if ( trace.Fraction <= 0 ) return position;
		if ( trace.Fraction >= 1 ) return position;
		if ( trace.StartedSolid ) return position;
		if ( Vector3.GetAngle( Vector3.Up, trace.Normal ) > GroundAngle ) return position;

		return trace.EndPosition;
	}

	public bool HasEvent( string eventName )
	{
		return ControllerEvents.Contains( eventName );
	}

	void AddEvent( string eventName )
	{
		if ( HasEvent( eventName ) )
			return;

		ControllerEvents.Add( eventName );
	}
}
