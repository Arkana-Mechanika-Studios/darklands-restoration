# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in French.


id-rw-money-florins =
    { $count ->
        [one] { $amount } florin
       *[other] { $amount } florins
    }

id-rw-money-groschen =
    { $count ->
       *[other] { $amount } gros
    }

id-rw-money-pfennigs =
    { $count ->
        [one] { $amount } denier
       *[other] { $amount } deniers
    }

id-rw-money-two = { $a } et { $b }

id-rw-money-three = { $a }, { $b } et { $c }
