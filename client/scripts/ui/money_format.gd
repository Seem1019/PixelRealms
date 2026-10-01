class_name MoneyFormat
## Oro en cobre → "12o 34p 56c" (rules.economy.copperPerSilver / silverPerGold), skill inventory-items §Cliente.


static func format(copper: int) -> String:
	var per_silver := int(Content.rule("economy", "copperPerSilver", 100))
	var per_gold := int(Content.rule("economy", "silverPerGold", 100))
	var c := copper % per_silver
	var silver_total := copper / per_silver
	var s := silver_total % per_gold
	var g := silver_total / per_gold
	var parts: Array[String] = []
	if g > 0:
		parts.append("%do" % g)
	if s > 0 or g > 0:
		parts.append("%dp" % s)
	parts.append("%dc" % c)
	return " ".join(parts)
