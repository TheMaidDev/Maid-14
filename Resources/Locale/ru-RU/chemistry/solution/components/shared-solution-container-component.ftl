shared-solution-container-component-on-examine-main-text = Содержит [color={ $color }]{ $desc }[/color] { $chemCount ->
    [1] химикат
   *[other] смесь химикатов
}
examinable-solution-has-recognizable-chemicals = В этом растворе вы можете распознать { $recognizedString }.
examinable-solution-recognized = [color={$color}]{$chemical}[/color]

examinable-solution-on-examine-volume = Содержимый раствор { $fillLevel ->
    [exact] содержит [color=white]{$current}/{$max}u[/color].
   *[other] [bold]{ -solution-vague-fill-level(fillLevel: $fillLevel) }[/bold].
}

examinable-solution-on-examine-volume-no-max = Содержимый раствор { $fillLevel ->
    [exact] содержит [color=white]{$current}u[/color].
   *[other] [bold]{ -solution-vague-fill-level(fillLevel: $fillLevel) }[/bold].
}

examinable-solution-on-examine-volume-puddle = Лужа { $fillLevel ->
    [exact] [color=white]{$current} ед.[/color]
    [full] огромна и разливается!
    [mostlyfull] огромна и разливается!
    [halffull] глубокая, течёт.
    [halfempty] очень глубокая.
   *[mostlyempty] собирается в лужи.
    [empty] образует несколько мелких луж.
}

-solution-vague-fill-level =
    { $fillLevel ->
        [full] [color=white]Полный[/color]
        [mostlyfull] [color=#DFDFDF]Почти полный[/color]
        [halffull] [color=#C8C8C8]Наполовину полный[/color]
        [halfempty] [color=#C8C8C8]Наполовину пустой[/color]
        [mostlyempty] [color=#A4A4A4]Почти пустой[/color]
       *[empty] [color=gray]Пустой[/color]
    }
