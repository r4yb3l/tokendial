# Lessons

- **Text next to an edge needs slack, not a computed width.** A StackPanel given `CardWidth - 2*Padding` ignored the 1 px border and clipped the last glyph of every right-aligned line ("Resets Tue 12:16 AM" lost its "M"). Let the body stretch inside the border and give right-aligned text a small margin; never hand-compute an inner width that the container already knows. Raised by the user on 2026-09-07 ("separa los límites del borde para que no se coma caracteres").
- **Fixture expectations are derived, not typed.** Five fixtures carried `resetsAt` values produced by a buggy epoch conversion; the C# parser was right and the spec was wrong. Generate expected values from the raw response fields with one script, and keep that script.
- **Do not move the user's cursor while they are working.** Synthetic hover tests fought a live desktop and produced noise. Prove hover with logs; take one screenshot when a cursor move is unavoidable.
