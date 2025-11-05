// Include the namespaces (code libraries) you need below.
using Raylib_cs;
using System;
using System.Numerics;
using System.Collections.Generic;

// The namespace your code is in.
namespace MohawkGame2D
{
    /// <summary>
    ///     Your game code goes inside this class!
    /// </summary>
    public class Game
    {
        // Player position
        float playerX = 100f;
        float playerY = 100f;

        // --- Tunables & constants ---
        readonly float speed = 200f;                 // input move speed (px/s)
        readonly float gravity = 1500f;              // px/s^2
        readonly float jumpSpeed = 600f;             // initial vertical jump velocity (px/s)
        readonly float wallJumpImpulse = 300f;       // horizontal push when wall-jumping
        readonly float horizontalFriction = 8f;      // how quickly horizontal impulse decays (larger = faster decay)

        // Gameplay feel improvements
        readonly float coyoteTime = 0.12f;           // allow jump shortly after leaving ground
        float coyoteTimer = 0f;
        readonly float jumpBufferTime = 0.12f;       // allow jump input slightly before landing
        float jumpBufferTimer = 0f;

        readonly int maxJumps = 2;
        int jumpCount = 0;

        // radius used for drawing and collision
        readonly float radius = 20f;

        // screen-related
        readonly float wallThickness = 20f;
        int screenWidth;
        int screenHeight;

        // physics state
        float velocityX = 0f;
        float velocityY = 0f;

        // simple walls (rectangles)
        Rectangle leftWall;
        Rectangle rightWall;

        // moving platforms
        List<MovingPlatform> platforms = new List<MovingPlatform>();

        // collectible (yellow ball)
        Vector2 collectiblePos = new Vector2();
        readonly float collectibleRadius = 8f;

        // TIME ORB: adds 30 seconds when collected
        Vector2 timeOrbPos = new Vector2();
        readonly float TimeOrbRadius = 10f;
        bool timeOrbActive = false;
        readonly float timeOrbInterval = 15f;    // attempt spawn every 15 seconds
        float timeOrbSpawnTimer = 0f;

        // scoring: number of collected yellow balls (called "coins")
        int coins = 0;

        // 2-minute timer (seconds)
        float timeRemaining = 120f;

        // Game state
        enum GameState { Playing, GameOver }
        GameState gameState = GameState.Playing;

        /// <summary>
        ///     Setup runs once before the game loop begins.
        /// </summary>
        public void Setup()
        {
            Window.SetTitle("Cronus Cash");
            Window.SetSize(800, 600);

            // initial screen cache
            screenWidth = Raylib.GetScreenWidth();
            screenHeight = Raylib.GetScreenHeight();

            // initialize walls
            leftWall = new Rectangle(0, 0, wallThickness, screenHeight);
            rightWall = new Rectangle(screenWidth - wallThickness, 0, wallThickness, screenHeight);

            // moving platforms (rect, axisMin, axisMax, velocity, horizontal)
            platforms.Add(new MovingPlatform(new Rectangle(200f, 450f, 240f, 16f), 200f, 600f, new Vector2(80f, 0f), true));
            platforms.Add(new MovingPlatform(new Rectangle(520f, 350f, 140f, 16f), 300f, 520f, new Vector2(-60f, 0f), true));
            platforms.Add(new MovingPlatform(new Rectangle(800f, 400f, 160f, 16f), 380f, 520f, new Vector2(0f, 40f), false));
            platforms.Add(new MovingPlatform(new Rectangle(100f, 250f, 120f, 16f), 100f, 600f, new Vector2(50f, 0f), true));

            // place the collectible
            RespawnCollectible();

            // initialize blue orb timer (attempt first spawn after interval)
            timeOrbSpawnTimer = timeOrbInterval;
            timeOrbActive = false;

            // ensure timer & state are initialized
            timeRemaining = 120f;
            gameState = GameState.Playing;
            coins = 0;
        }

        /// <summary>
        ///     Update runs every frame.
        /// </summary>
        public void Update()
        {
            // per-frame time
            float dt = Raylib.GetFrameTime();

            // update cached screen size once per frame
            screenWidth = Raylib.GetScreenWidth();
            screenHeight = Raylib.GetScreenHeight();

            // If game over, show game over screen and wait for restart
            if (gameState == GameState.GameOver)
            {
                // Draw overlay
                Window.ClearBackground(Color.LightGray);
                string title = "GOOD JOB!";
                int titleSize = 48;
                int titleW = Raylib.MeasureText(title, titleSize);
                Raylib.DrawText(title, (screenWidth - titleW) / 2, screenHeight / 2 - 80, titleSize, Color.Yellow);

                string coinsText = $"Coins: {coins}";
                int coinsSize = 32;
                int coinsW = Raylib.MeasureText(coinsText, coinsSize);
                Raylib.DrawText(coinsText, (screenWidth - coinsW) / 2, screenHeight / 2 - 20, coinsSize, Color.OffWhite);

                string instr = "Press R to restart";
                int instrSize = 20;
                int instrW = Raylib.MeasureText(instr, instrSize);
                Raylib.DrawText(instr, (screenWidth - instrW) / 2, screenHeight / 2 + 40, instrSize, Color.DarkGray);

                // restart on R
                if (Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    Restart();
                }

                return;
            }

            // --- Playing state ---

            // update walls to follow screen size
            leftWall = new Rectangle(0, 0, wallThickness, screenHeight);
            rightWall = new Rectangle(screenWidth - wallThickness, 0, wallThickness, screenHeight);

            // Update platforms first
            foreach (var p in platforms) p.Update(dt);

            // Save previous Y for platform landing checks
            float prevY = playerY;

            // Input
            float inputX = 0f;
            if (Raylib.IsKeyDown(KeyboardKey.A)) inputX -= 1f;
            if (Raylib.IsKeyDown(KeyboardKey.D)) inputX += 1f;

            // Jump buffering
            if (Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Space))
                jumpBufferTimer = jumpBufferTime;
            else
                jumpBufferTimer -= dt;

            // Move by direct input
            playerX += inputX * speed * dt;

            // Apply horizontal impulse velocity (e.g., wall-jump pushes)
            playerX += velocityX * dt;

            // Gravity + vertical integration
            velocityY += gravity * dt;
            playerY += velocityY * dt;

            // Timer update
            timeRemaining -= dt;
            if (timeRemaining <= 0f)
            {
                timeRemaining = 0f;
                gameState = GameState.GameOver;
            }

            // --- Time Orb spawn logic ---
            // Attempt spawn every TimeOrbInterval seconds with 1-in-3 chance
            timeOrbSpawnTimer -= dt;
            if (timeOrbSpawnTimer <= 0f)
            {
                timeOrbSpawnTimer = timeOrbInterval; // reset timer
                if (!timeOrbActive)
                {
                    // 1 in 3 chance (33.3%)
                    if (Raylib.GetRandomValue(0, 2) == 0)
                    {
                        // try to find a valid spawn position
                        for (int attempt = 0; attempt < 200; attempt++)
                        {
                            Vector2 p = Random.Vector2(TimeOrbRadius, screenWidth - TimeOrbRadius, TimeOrbRadius, screenHeight - TimeOrbRadius);
                            // avoid inside walls
                            if (Raylib.CheckCollisionCircleRec(p, TimeOrbRadius, leftWall)) continue;
                            if (Raylib.CheckCollisionCircleRec(p, TimeOrbRadius, rightWall)) continue;
                            // avoid platforms
                            bool bad = false;
                            foreach (var plat in platforms)
                            {
                                if (Raylib.CheckCollisionCircleRec(p, TimeOrbRadius, plat.Rect)) { bad = true; break; }
                            }
                            if (bad) continue;
                            // avoid too close to player
                            if (Vector2.Distance(p, new Vector2(playerX, playerY)) < (radius + TimeOrbRadius + 30f)) continue;

                            timeOrbPos = p;
                            timeOrbActive = true;
                            break;
                        }
                    }
                }
            }

            // Ground detection
            float groundY = screenHeight - radius;
            bool onGround = false;
            if (playerY >= groundY)
            {
                playerY = groundY;
                velocityY = 0f;
                velocityX = 0f;          // STOP horizontal impulse when landing on ground
                jumpCount = 0;
                onGround = true;
                coyoteTimer = coyoteTime; // grant coyote time when on ground
            }
            else
            {
                // coyote timer ticks down when not on ground
                coyoteTimer -= dt;
            }

            // Platform collisions: landing from above or side separation
            foreach (var plat in platforms)
            {
                if (Raylib.CheckCollisionCircleRec(new Vector2(playerX, playerY), radius, plat.Rect))
                {
                    float platformTop = plat.Rect.Y;
                    float platformLeft = plat.Rect.X;
                    float platformRight = plat.Rect.X + plat.Rect.Width;

                    // If previous bottom was above platform top, treat as landing from above
                    if (prevY + radius <= platformTop + 1f && playerY + radius >= platformTop)
                    {
                        playerY = platformTop - radius;
                        velocityY = 0f;
                        velocityX = 0f;     // STOP horizontal impulse when landing on a platform
                        jumpCount = 0;
                        onGround = true;
                        coyoteTimer = coyoteTime;

                        // carry horizontally with platform movement (position update only)
                        playerX += plat.Velocity.X * dt;
                    }
                    else
                    {
                        // side collision handling: push out horizontally
                        if (playerX < platformLeft)
                        {
                            playerX = platformLeft - radius;
                            velocityX = 0f;
                        }
                        else if (playerX > platformRight)
                        {
                            playerX = platformRight + radius;
                            velocityX = 0f;
                        }
                        else
                        {
                            // fallback: push player above platform if somehow inside vertically
                            playerY = platformTop - radius;
                            velocityY = 0f;
                            velocityX = 0f;
                            onGround = true;
                            coyoteTimer = coyoteTime;
                        }
                    }
                }
            }

            // Wall contacts for wall-jump detection (left/right walls only)
            bool touchingLeftWall = Raylib.CheckCollisionCircleRec(new Vector2(playerX, playerY), radius, leftWall);
            bool touchingRightWall = Raylib.CheckCollisionCircleRec(new Vector2(playerX, playerY), radius, rightWall);
            bool onWall = (touchingLeftWall || touchingRightWall) && !onGround;

            // If touching wall (not ground) we also allow coyote-like wall grace so wall-jump is responsive
            if (onWall)
            {
                coyoteTimer = MathF.Max(coyoteTimer, 0f); // keep existing coyote (no change) but explicit
            }

            // Jump logic: use jump buffer and coyote time for responsive jumping
            bool canJump = (jumpCount < maxJumps) || onWall || (coyoteTimer > 0f);
            if (jumpBufferTimer > 0f && canJump)
            {
                // perform jump
                velocityY = -jumpSpeed;

                // if this is a wall-jump, grant the player one used jump (so a follow-up jump remains available)
                if (onWall)
                {
                    jumpCount = 1; // treat wall-jump as first jump used => still allow one more jump (double jump)
                }
                else
                {
                    jumpCount++;
                }

                jumpBufferTimer = 0f;
                coyoteTimer = 0f; // consume coyote time

                // wall jump impulse if jumping from a wall
                if (onWall)
                {
                    if (touchingLeftWall)
                    {
                        velocityX = wallJumpImpulse; // push right
                        playerX = leftWall.X + leftWall.Width + radius + 0.5f;
                    }
                    else if (touchingRightWall)
                    {
                        velocityX = -wallJumpImpulse; // push left
                        playerX = rightWall.X - radius - 0.5f;
                    }
                }
            }

            // Check collectible pickup
            if (Raylib.CheckCollisionCircles(new Vector2(playerX, playerY), radius, collectiblePos, collectibleRadius))
            {
                coins++;                 // increment coin counter when collected
                RespawnCollectible();
            }

            // Check blue orb pickup
            if (timeOrbActive && Raylib.CheckCollisionCircles(new Vector2(playerX, playerY), radius, timeOrbPos, TimeOrbRadius))
            {
                // award +30 seconds
                timeRemaining += 30f;
                timeOrbActive = false;
                // reset spawn timer so next chance happens after full interval
                timeOrbSpawnTimer = timeOrbInterval;
            }

            // Wall collision resolution (prevent penetrating)
            if (Raylib.CheckCollisionCircleRec(new Vector2(playerX, playerY), radius, leftWall))
            {
                playerX = leftWall.X + leftWall.Width + radius;
                velocityX = 0f;
            }
            if (Raylib.CheckCollisionCircleRec(new Vector2(playerX, playerY), radius, rightWall))
            {
                playerX = rightWall.X - radius;
                velocityX = 0f;
            }

            // Clamp horizontal position so the circle stays visible
            playerX = Math.Clamp(playerX, radius, screenWidth - radius);

            // Draw everything
            Window.ClearBackground(Color.OffWhite);

            // Draw walls (visual aid)
            Raylib.DrawRectangleRec(leftWall, Color.DarkGray);
            Raylib.DrawRectangleRec(rightWall, Color.DarkGray);

            // Draw moving platforms
            foreach (var p in platforms) Raylib.DrawRectangleRec(p.Rect, Color.Black);

            // Draw collectible (yellow ball)
            Raylib.DrawCircleV(collectiblePos, collectibleRadius, Color.Yellow);

            // Draw blue orb if active
            if (timeOrbActive)
            {
                Raylib.DrawCircleV(timeOrbPos, TimeOrbRadius, Color.Blue);
                // small glow ring
                Raylib.DrawCircleLines((int)timeOrbPos.X, (int)timeOrbPos.Y, TimeOrbRadius + 4f, Color.Cyan);
            }

            // Draw player
            Draw.FillColor = Color.Red;
            Draw.Circle(playerX, playerY, radius);

            // HUD: coins and time (mm:ss)
            Raylib.DrawText($"Coins: {coins}", 16, 16, 24, Color.Black);
            int minutes = (int)timeRemaining / 60;
            int seconds = (int)timeRemaining % 60;
            string timeText = $"{minutes:D2}:{seconds:D2}";
            int tw = Raylib.MeasureText(timeText, 24);
            Raylib.DrawText(timeText, screenWidth - tw - 16, 16, 24, Color.Black);
        }

        void Restart()
        {
            // reset basic game state
            playerX = 100f;
            playerY = 100f;
            velocityX = 0f;
            velocityY = 0f;
            jumpCount = 0;
            coins = 0;
            timeRemaining = 120f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;

            // respawn collectible and reset orb state
            RespawnCollectible();
            timeOrbActive = false;
            timeOrbSpawnTimer = timeOrbInterval;

            gameState = GameState.Playing;
        }

        // Respawn collectible at a random valid location (uses provided Random helper)
        void RespawnCollectible()
        {
            // update screen bounds to use current size
            screenWidth = Raylib.GetScreenWidth();
            screenHeight = Raylib.GetScreenHeight() - 50;

            for (int attempt = 0; attempt < 500; attempt++)
            {
                // Use the project's Random helper to get a random Vector2 inside the play area
                Vector2 p = Random.Vector2(collectibleRadius, screenWidth - collectibleRadius, collectibleRadius, screenHeight - collectibleRadius);

                // avoid spawning inside walls
                if (Raylib.CheckCollisionCircleRec(p, collectibleRadius, leftWall)) continue;
                if (Raylib.CheckCollisionCircleRec(p, collectibleRadius, rightWall)) continue;

                // avoid spawning inside any platform
                bool bad = false;
                foreach (var plat in platforms)
                {
                    if (Raylib.CheckCollisionCircleRec(p, collectibleRadius, plat.Rect)) { bad = true; break; }
                }
                if (bad) continue;

                // avoid spawning too close to the player
                if (Vector2.Distance(p, new Vector2(playerX, playerY)) < (radius + collectibleRadius + 20f)) continue;

                collectiblePos = p;
                return;
            }

            // fallback if no valid position found
            collectiblePos = new Vector2(screenWidth * 0.5f, screenHeight * 0.5f);
        }

        // Nested helper class for moving platforms
        class MovingPlatform
        {
            public Rectangle Rect;
            public Vector2 Velocity;
            float minAxis;
            float maxAxis;
            bool horizontal; // true: move on X axis, false: move on Y axis

            public MovingPlatform(Rectangle rect, float minAxis, float maxAxis, Vector2 velocity, bool horizontal)
            {
                Rect = rect;
                this.minAxis = minAxis;
                this.maxAxis = maxAxis;
                Velocity = velocity;
                this.horizontal = horizontal;
            }

            public void Update(float dt)
            {
                if (horizontal)
                {
                    Rect.X += Velocity.X * dt;
                    if (Rect.X < minAxis)
                    {
                        Rect.X = minAxis;
                        Velocity.X = -Velocity.X;
                    }
                    else if (Rect.X > maxAxis)
                    {
                        Rect.X = maxAxis;
                        Velocity.X = -Velocity.X;
                    }
                }
                else
                {
                    Rect.Y += Velocity.Y * dt;
                    if (Rect.Y < minAxis)
                    {
                        Rect.Y = minAxis;
                        Velocity.Y = -Velocity.Y;
                    }
                    else if (Rect.Y > maxAxis)
                    {
                        Rect.Y = maxAxis;
                        Velocity.Y = -Velocity.Y;
                    }
                }
            }
        }
    }
}