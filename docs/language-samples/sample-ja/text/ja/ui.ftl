# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Japanese.


id-rw-money-florins =
    { $count ->
       *[other] { $amount }フローリン
    }

id-rw-money-groschen =
    { $count ->
       *[other] { $amount }グロッシェン
    }

id-rw-money-pfennigs =
    { $count ->
       *[other] { $amount }ペニヒ
    }

id-rw-money-two = { $a }{ $b }

id-rw-money-three = { $a }{ $b }{ $c }
