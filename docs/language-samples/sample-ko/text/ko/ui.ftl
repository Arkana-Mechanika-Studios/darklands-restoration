# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Korean.


id-rw-money-florins =
    { $count ->
       *[other] { $amount }플로린
    }

id-rw-money-groschen =
    { $count ->
       *[other] { $amount }그로셴
    }

id-rw-money-pfennigs =
    { $count ->
       *[other] { $amount }페니히
    }

id-rw-money-two = { $a } { $b }

id-rw-money-three = { $a } { $b } { $c }
